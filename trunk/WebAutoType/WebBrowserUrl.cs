using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows.Automation;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;


namespace WebAutoType
{
	/// <summary>
	/// Class to retrive URL from specified window (if it's a browser window)
	/// </summary>
	public static class WebBrowserUrl
	{
		[DllImport("user32.dll", EntryPoint = "FindWindowEx", CharSet = CharSet.Auto)]
		static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string lpszWindow);

		[DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
		static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

		// When Chrome enables accessibility, it takes a little while to enable. This value controls how often we poll to see if it's ready yet.
		private static readonly TimeSpan ChromeRePollInterval = TimeSpan.FromMilliseconds(100);

		private static string[] SupportedTopLevelWindowClasses = new[] 
		{
			"MozillaUIWindowClass",
			"MozillaWindowClass",
			"IEFrame",
			"OperaWindowClass",
			"ApplicationFrameWindow", // Edge, or, unfortunately, any Metro app
			// Chrome may append any number to this, but to search for a specific class name, which can't use wildcards, just use the first few.
			"Chrome_WidgetWin_0",
			"Chrome_WidgetWin_1",
			"Chrome_WidgetWin_2",
			"Chrome_WidgetWin_3",
		};

		public static IEnumerable<AutomationElement> GetTopLevelBrowserWindows()
		{
			return AutomationElement.RootElement.FindAll(TreeScope.Children, new OrCondition((from className in SupportedTopLevelWindowClasses
																								select new PropertyCondition(AutomationElement.ClassNameProperty, className)).ToArray())).Cast<AutomationElement>();
		}

		public static string GetBrowserUrl(AutomationElement window)
		{
			if( window != null )
			{
				if ( window.Current.ClassName == "MozillaUIWindowClass" ) // FF 3.6
				{
					window = window.FindFirst( TreeScope.Descendants, new PropertyCondition( AutomationElement.ClassNameProperty, "MozillaContentWindowClass" ) );
					if( window != null )
					{
						var valuePattern = window.GetCurrentPattern( ValuePattern.Pattern ) as ValuePattern;
						return valuePattern != null ? valuePattern.Current.Value  : null;
					}
				}
				else if ( window.Current.ClassName == "MozillaWindowClass" ) // new FF, 8.0 etc
				{
					window = FindActiveFFDocument( window );

					if ( window != null )
					{
						var valuePattern = window.GetCurrentPattern( ValuePattern.Pattern ) as ValuePattern;
						return valuePattern != null ? valuePattern.Current.Value : null;
					}
				}
				else if( window.Current.ClassName == "IEFrame" )
				{
					window = window.FindFirst( TreeScope.Descendants, new PropertyCondition( AutomationElement.ClassNameProperty, "Internet Explorer_Server" ) );
					if( window != null )
					{
						var valuePattern = window.GetCurrentPattern( ValuePattern.Pattern ) as ValuePattern;
						return valuePattern != null ? valuePattern.Current.Value : null;
					}
				}
				else if( window.Current.ClassName.StartsWith( "Chrome_WidgetWin_" ) )
				{
					// Chrome > 32
					var renderWidgetHost = window.FindFirst(TreeScope.Children, new PropertyCondition(AutomationElement.ClassNameProperty, "Chrome_RenderWidgetHostHWND"));
					if (renderWidgetHost != null)
					{
						var value = GetValueOrDefault(renderWidgetHost, null);
						if (value != null)
						{
							return value;
						}
					}

					// Chrome > 29
					var toolbar = window.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ToolBar));

					if (toolbar != null)
					{
						var editBox = toolbar.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));

						if (editBox != null)
						{
							var valuePattern = editBox.GetCurrentPattern(ValuePattern.Pattern) as ValuePattern;
							return valuePattern != null ? valuePattern.Current.Value : null;
						}

						return null;
					}

					// Chrome < 29
					window = window.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.ClassNameProperty, "Chrome_OmniboxView"));
					if (window != null)
					{
						var valuePattern = window.GetCurrentPattern(ValuePattern.Pattern) as ValuePattern;
						return valuePattern != null ? valuePattern.Current.Value : null;
					}
				}
				else if ( window.Current.ClassName == "OperaWindowClass" )
				{
					var arrToolbars = window.FindAll( TreeScope.Descendants, new PropertyCondition( AutomationElement.ControlTypeProperty, ControlType.ToolBar ) );
					if ( arrToolbars.Count > 0 )
					{
						foreach ( AutomationElement toolbar in arrToolbars )
						{
							var arrCombos = toolbar.FindAll( TreeScope.Descendants, new PropertyCondition( AutomationElement.ControlTypeProperty, ControlType.ComboBox ) );
							if ( arrCombos.Count > 0 )
							{
								foreach ( AutomationElement combo in arrCombos )
								{
									var arrButtons = combo.FindAll( TreeScope.Children, new PropertyCondition( AutomationElement.ControlTypeProperty, ControlType.Button ) );
									var arrSplitButtons = combo.FindAll( TreeScope.Children, new PropertyCondition( AutomationElement.ControlTypeProperty, ControlType.SplitButton ) );
									var arrEdits = combo.FindAll( TreeScope.Children, new PropertyCondition( AutomationElement.ControlTypeProperty, ControlType.Edit ) );
									if ( arrButtons.Count > 1 && arrSplitButtons.Count == 1 && arrEdits.Count == 1 ) // looks like an opera URL edit box....
									{
										var valuePattern = arrEdits[0].GetCurrentPattern( ValuePattern.Pattern ) as ValuePattern;
										return valuePattern != null ? valuePattern.Current.Value : null;
									}
								}
							}
						}
					}
				}
				else if (window.Current.ClassName == "ApplicationFrameWindow") // Edge (or, unfortunately, any other Metro app))
				{
					window = window.FindFirst(TreeScope.Children, new AndCondition(new PropertyCondition(AutomationElement.ClassNameProperty, "Windows.UI.Core.CoreWindow"), new PropertyCondition(AutomationElement.NameProperty, "Microsoft Edge")));
					if (window != null)
					{
						// Search for an "Internet Explorer_Server" window as a child of a "TabWindowClass" by hwnd descendants
						foreach (var tabWindowHandle in FindDescendantWindows((IntPtr)window.Current.NativeWindowHandle, "TabWindowClass"))
						{
							if (AutomationElement.FromHandle(tabWindowHandle).Current.IsEnabled)
							{
								var ieWindowHandle = FindWindowEx(tabWindowHandle, IntPtr.Zero, "Windows.UI.Core.CoreComponentInputSource", null);
								if (ieWindowHandle != IntPtr.Zero)
								{
									var ieWindow = AutomationElement.FromHandle(ieWindowHandle);

									if (ieWindow.Current.ClassName == "Internet Explorer_Server")
									{
										var url = ieWindow.Current.Name;
										if (!String.IsNullOrEmpty(url))
										{
											return url;
										}
									}
								}
							}
						}
					}
				}
			}
			return null;
		}

		internal static IEnumerable<IntPtr> FindDescendantWindows(IntPtr windowHandle, string className)
		{
			var results = new List<IntPtr>();

			// Recurse into children
			var anyChildren = false;
			// First list any results
			var childWindowHandle = IntPtr.Zero;
			do
			{
				childWindowHandle = FindWindowEx(windowHandle, childWindowHandle, null, null);
				if (childWindowHandle != IntPtr.Zero)
				{
					anyChildren = true;
					results.AddRange(FindDescendantWindows(childWindowHandle, className));
				}
			} while (childWindowHandle != IntPtr.Zero);

			if (anyChildren)
			{
				// Now add any results at this level
				childWindowHandle = IntPtr.Zero;
				do
				{
					childWindowHandle = FindWindowEx(windowHandle, childWindowHandle, className, null);
					if (childWindowHandle != IntPtr.Zero)
					{
						results.Add(childWindowHandle);
					}
				} while (childWindowHandle != IntPtr.Zero);
			}

			return results;
		}

		/// <summary>
		/// Gets the URL from the browser with the current focus. If there is no current focus, falls back on trying to get the active URL from
		/// the fallback top-level window handle specified.
		/// 
		/// If the current focus is detected to be in a password field, passwordFieldFocussed is set true.
		/// </summary>
		internal static string GetFocusedBrowserUrl(ChromeAccessibilityWinEventHook chromeAccessibility, IntPtr fallbackWindowHandle, out bool passwordFieldFocussed)
		{
			try
			{
				var timeout = TimeSpan.FromSeconds(1);

				AutomationElement focusedElement = null;

				var stopWatch = Stopwatch.StartNew();
				var rootElement = AutomationElement.RootElement;

				do
				{
					if (stopWatch.Elapsed > timeout)
					{
						// Could not get the focused element through UIA.
						passwordFieldFocussed = false;
						return fallbackWindowHandle == IntPtr.Zero ? null : GetBrowserUrl(AutomationElement.FromHandle(fallbackWindowHandle));
					}

					focusedElement = GetFocusedElement(chromeAccessibility);
				} while (focusedElement == null || focusedElement == rootElement);

				// It's unlikely that we don't want an edit box of some sort, so give it an extra chance to get one
				stopWatch.Reset();
				stopWatch.Start();
				while (stopWatch.Elapsed < timeout && 
							focusedElement.Current.ControlType != ControlType.Edit &&
							!IsChromeWindowWithNoUIA(focusedElement))
				{
					focusedElement = GetFocusedElement(chromeAccessibility) ?? focusedElement;
				}

				// Special check for Chrome (and others?) not returning actual focused element
				if (focusedElement.Current.ControlType != ControlType.Edit && !focusedElement.Current.HasKeyboardFocus)
				{
					// Find the actual focused element as a child of the one given. Slow.
					focusedElement = focusedElement.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.HasKeyboardFocusProperty, true)) ?? focusedElement;
				}

				passwordFieldFocussed = focusedElement.Current.IsPassword;

				// Firefox
				var parentDocument = AncestorsOrSelf(focusedElement).FirstOrDefault(element => element.Current.ControlType == ControlType.Document);
				if (parentDocument != null)
				{
					var url = GetValueOrDefault(parentDocument, null);
					
					if (url != null)
					{
						return url;
					}
				}

				// TODO: Other browsers?

				// Fall back on general case
				return fallbackWindowHandle == IntPtr.Zero ? null : GetBrowserUrl(AutomationElement.FromHandle(fallbackWindowHandle));
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine("UIA Failure: " + ex.Message);
				passwordFieldFocussed = false;
				return fallbackWindowHandle == IntPtr.Zero ? null : GetBrowserUrl(AutomationElement.FromHandle(fallbackWindowHandle));
			}
		}

		private static AutomationElement GetFocusedElement(ChromeAccessibilityWinEventHook chromeAccessibility)
		{
			chromeAccessibility.EventReceived = false;
			var focusedElement = AutomationElement.FocusedElement;

			// If Chrome accessibility received an event, then Chrome has just turned on accessibility, so re-query for the focused element now that Chrome will actually provide it.
			if (chromeAccessibility.EventReceived)
			{
				var oldFocusedElement = focusedElement;
				var stopWatch = Stopwatch.StartNew();
				var timeout = TimeSpan.FromSeconds(1);
				while (stopWatch.Elapsed < timeout &&
				       focusedElement == oldFocusedElement)
				{
					// Wait a bit and re-query for the focused element. Now that accessibility is turned on, it shouldn't be the same.
					Thread.Sleep(ChromeRePollInterval);
					focusedElement = AutomationElement.FocusedElement;
				}
			}
			
			return focusedElement;
		}

		private static bool IsChromeWindowWithNoUIA(AutomationElement focusedElement)
		{
			return focusedElement.Current.ClassName == "Chrome_RenderWidgetHostHWND" && !(bool)focusedElement.GetCurrentPropertyValue(AutomationElement.IsValuePatternAvailableProperty);
		}

		internal static bool GetFocusedBrowserInfo(ChromeAccessibilityWinEventHook chromeAccessibility, out string selectedText, out string url, out string title)
		{
			selectedText = null;
			url = null;
			title = null;

			var focusedElement = GetFocusedElement(chromeAccessibility);
			if( focusedElement == null )
			{
				return false;
			}

			// Get text
			if (focusedElement.Current.ControlType == ControlType.Edit)
			{
				selectedText = GetValueOrDefault(focusedElement, null);
			}
			
			// Get URL - first just try Firefox
			var ffDocument = AncestorsOrSelf(focusedElement).FirstOrDefault(parent => parent.Current.ControlType == ControlType.Document);
			if (ffDocument != null)
			{
				url = GetValueOrDefault(ffDocument, null);
				title = ffDocument.Current.Name;
			}
			// TODO: Other browsers?

			if (url == null)
			{
				// Fall back on general case
				url = GetBrowserUrl(AncestorsOrSelf(focusedElement).Last());
			}

			return true;
		}

		private static IEnumerable<AutomationElement> AncestorsOrSelf(AutomationElement element)
		{
			TreeWalker walker = TreeWalker.ControlViewWalker;

			var rootElement = AutomationElement.RootElement;
			while (element != rootElement)
			{
				yield return element;
				element = walker.GetParent(element);
			}
		}

		private static string GetValueOrDefault(AutomationElement element, string defaultValue)
		{
			object valueElementObject;
			if (element.TryGetCurrentPattern(ValuePattern.Pattern, out valueElementObject))
			{
				var valuePattern = valueElementObject as ValuePattern;
				if (valuePattern != null)
				{
					return valuePattern.Current.Value;
				}
			}
			return defaultValue;
		}

		private static AutomationElement FindActiveFFDocument( AutomationElement element )
		{
/*
			if ( (bool) element.GetCurrentPropertyValue( AutomationElement.IsOffscreenProperty ) )
			{
				return null;
			}

			if ( element.GetCurrentPropertyValue( AutomationElement.ControlTypeProperty ) == ControlType.Document )
			{
				return element;
			}

			var el = TreeWalker.ContentViewWalker.GetFirstChild( element );

			while ( el != null )
			{
				var elReturn = FindActiveFFDocument( el );
				if ( elReturn != null )
				{
					return elReturn;
				}
				el = TreeWalker.ContentViewWalker.GetNextSibling( el );
			}

			return null;
*/

			var arrChildren = element.FindAll( TreeScope.Children, new AndCondition(
					new PropertyCondition( AutomationElement.IsContentElementProperty, true ),
					new PropertyCondition( AutomationElement.IsOffscreenProperty, false ),
					new OrCondition(
						new PropertyCondition( AutomationElement.ControlTypeProperty, ControlType.Custom ),
						new PropertyCondition( AutomationElement.ControlTypeProperty, ControlType.Document )
						)
				) );

			foreach ( AutomationElement automationElement in arrChildren )
			{
				if ( automationElement.GetCurrentPropertyValue( AutomationElement.ControlTypeProperty ) == ControlType.Document )
				{
					return automationElement;
				}

				var elemRet = FindActiveFFDocument( automationElement );
				if ( elemRet != null )
				{
					return elemRet;
				}
			}
			
			return null;

		}


		public static bool IsWindowHandleSupportedBrowser(IntPtr hWnd)
		{
			// Pre-allocate 256 characters, since this is the maximum class name length.
			var classNameBuilder = new StringBuilder(256);
			//Get the window class name
			GetClassName(hWnd, classNameBuilder, classNameBuilder.Capacity);
			var className = classNameBuilder.ToString();

			if (SupportedTopLevelWindowClasses.Contains(className) ||
				className.StartsWith("Chrome_WidgetWin_")) // Special case for Chrome which may append any number to the class name
			{
				return true;
			}

			return false;
		}
	}
}
