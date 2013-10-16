using System;
using System.Linq;
using System.Windows.Automation;
using System.Collections.Generic;


namespace WebAutoType
{
	/// <summary>
	/// Class to retrive URL from specified window (if it's a browser window)
	/// </summary>
	public static class WebBrowserUrl
	{
		/// <summary>
		/// Currently using UIAutomation to get URL
		/// </summary>
		/// <param name="hWnd">Window handle</param>
		/// <returns>URL if found, null otherwise</returns>
		public static string GetBrowserUrl( IntPtr hWnd )
		{
			if( hWnd == IntPtr.Zero )
			{
				return null;
			}

			AutomationElement el = AutomationElement.FromHandle( hWnd );
			if( el != null )
			{
				if ( el.Current.ClassName == "MozillaUIWindowClass" ) // FF 3.6
				{
					el = el.FindFirst( TreeScope.Descendants, new PropertyCondition( AutomationElement.ClassNameProperty, "MozillaContentWindowClass" ) );
					if( el != null )
					{
						var valuePattern = el.GetCurrentPattern( ValuePattern.Pattern ) as ValuePattern;
						return valuePattern != null ? valuePattern.Current.Value  : null;
					}
				}
				else if ( el.Current.ClassName == "MozillaWindowClass" ) // new FF, 8.0 etc
				{
					el = FindActiveFFDocument( el );

					if ( el != null )
					{
						var valuePattern = el.GetCurrentPattern( ValuePattern.Pattern ) as ValuePattern;
						return valuePattern != null ? valuePattern.Current.Value : null;
					}
				}
				else if( el.Current.ClassName == "IEFrame" )
				{
					el = el.FindFirst( TreeScope.Descendants, new PropertyCondition( AutomationElement.ClassNameProperty, "Internet Explorer_Server" ) );
					if( el != null )
					{
						var valuePattern = el.GetCurrentPattern( ValuePattern.Pattern ) as ValuePattern;
						return valuePattern != null ? valuePattern.Current.Value : null;
					}
				}
				else if( el.Current.ClassName.StartsWith( "Chrome_WidgetWin_" ) )
				{
					// Chrome 29
					var toolbar = el.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ToolBar));

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
					el = el.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.ClassNameProperty, "Chrome_OmniboxView"));
					if (el != null)
					{
						var valuePattern = el.GetCurrentPattern(ValuePattern.Pattern) as ValuePattern;
						return valuePattern != null ? valuePattern.Current.Value : null;
					}
				}
				else if ( el.Current.ClassName == "OperaWindowClass" )
				{
					var arrToolbars = el.FindAll( TreeScope.Descendants, new PropertyCondition( AutomationElement.ControlTypeProperty, ControlType.ToolBar ) );
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
			}
			return null;
		}

		/// <summary>
		/// Gets the URL from the browser with the current focus. If there is no current focus, falls back on trying to get the active URL from
		/// the fallback top-level window handle specified.
		/// 
		/// If the current focus is detected to be in a password field, passwordFieldFocussed is set true.
		/// </summary>
		public static string GetFocussedBrowserUrl(IntPtr fallbackWindowHandle, out bool passwordFieldFocussed)
		{
			try
			{
				var timeout = TimeSpan.FromSeconds(1);

				AutomationElement focusedElement = null;

				var stopWatch = System.Diagnostics.Stopwatch.StartNew();
				do
				{
					focusedElement = AutomationElement.FocusedElement;
					if (stopWatch.Elapsed > timeout)
					{
						// Could not get the focused element through UIA.
						passwordFieldFocussed = false;
						return GetBrowserUrl(fallbackWindowHandle);
					}
				} while (focusedElement == null || focusedElement == AutomationElement.RootElement);

				// It's unlikely that we don't want an edit box of some sort, so give it an extra chance to get one
				stopWatch.Reset();
				stopWatch.Start();
				while (stopWatch.Elapsed < timeout &&
						focusedElement != null && focusedElement.Current.ControlType != ControlType.Edit &&
												  !IsChromeWindowWithNoUIA(focusedElement))
				{
					focusedElement = AutomationElement.FocusedElement;
				}

				passwordFieldFocussed = focusedElement.Current.IsPassword;

				var ffDocument = AncestorsOrSelf(focusedElement).FirstOrDefault(element => element.Current.ControlType == ControlType.Document);
				if (ffDocument != null)
				{
					var url = GetValueOrDefault(ffDocument, null);
					if (url != null)
					{
						return url;
					}
				}

				// TODO: Other browsers

				// Fall back on general case
				return GetBrowserUrl((IntPtr)AncestorsOrSelf(focusedElement).Last().Current.NativeWindowHandle);
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine("UIA Failure: " + ex.Message);
				passwordFieldFocussed = false;
				return GetBrowserUrl(fallbackWindowHandle);
			}
		}

		private static bool IsChromeWindowWithNoUIA(AutomationElement focusedElement)
		{
			return focusedElement.Current.ClassName == "Chrome_RenderWidgetHostHWND" && !(bool)focusedElement.GetCurrentPropertyValue(AutomationElement.IsValuePatternAvailableProperty);
		}

		public static bool GetFocussedBrowserInfo(out string selectedText, out string url, out string title)
		{
			selectedText = null;
			url = null;
			title = null;

			var focusedElement = AutomationElement.FocusedElement;
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
				url = GetBrowserUrl((IntPtr)AncestorsOrSelf(focusedElement).Last().Current.NativeWindowHandle);
			}

			return true;
		}

		private static IEnumerable<AutomationElement> AncestorsOrSelf(AutomationElement element)
		{
			TreeWalker walker = TreeWalker.ControlViewWalker;

			while (element != AutomationElement.RootElement)
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


	}
}
