using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using KeePass;
using KeePass.Plugins;
using KeePass.Forms;
using KeePass.UI;
using KeePass.Util;
using KeePass.Util.Spr;
using KeePassLib.Collections;
using KeePassLib.Utility;
using KeePassLib;
using KeePassLib.Security;
using KeePassLib.Cryptography.PasswordGenerator;

namespace WebAutoType
{
	/// <summary>
	/// 
	/// </summary>
	public sealed class WebAutoTypeExt : Plugin
	{
		private IPluginHost m_host;
		private const string c_sUrlPrefix = "??:URL:";
		private const string OptionsConfigRoot = "WebAutoType.";
		private const string UserNameAutoTypeSequenceStart = "{USERNAME}{TAB}";

		private Dictionary<int,List<string>> m_dicStrings = new Dictionary<int, List<string>>();
		private Dictionary<int, bool> mSkipUserNameForSequence = new Dictionary<int, bool>();
		private EditAutoTypeItemForm m_fEditForm;
		private string m_sLblText = string.Empty;
		private ToolStripMenuItem mOptionsMenu;
		private int mCreateEntryHotkeyId;

		private readonly HashSet<int> mFoundSequence = new HashSet<int>();
		private readonly HashSet<string> mUnfoundUrls = new HashSet<string>();
		private FieldInfo mSearchTextBoxField;

		private ChromeAccessibilityWinEventHook mChromeAccessibility;

		public override string UpdateUrl
		{
			get { return "sourceforge-version://WebAutoType/webautotype?-v(%5B%5Cd.%5D%2B)%5C.zip"; }
		}

		public override bool Initialize(IPluginHost host)
		{
			Debug.Assert( host != null );
			if( host == null )
				return false;
			
			m_host = host;

			GlobalWindowManager.WindowAdded += GlobalWindowManager_WindowAdded;
			AutoType.SequenceQuery += AutoType_SequenceQuery;
			AutoType.SequenceQueriesBegin += AutoType_SequenceQueriesBegin;
			AutoType.SequenceQueriesEnd += AutoType_SequenceQueriesEnd;

			mOptionsMenu = new ToolStripMenuItem
			{
				Text = Properties.Resources.OptionsMenuItemText,
			};
			mOptionsMenu.Click += mOptionsMenu_Click;

			m_host.MainWindow.ToolsMenu.DropDownItems.Add(mOptionsMenu);

			HotKeyManager.HotKeyPressed += HotKeyManager_HotKeyPressed;

			if (CreateEntryHotKey != Keys.None)
			{
				mCreateEntryHotkeyId = HotKeyManager.RegisterHotKey(CreateEntryHotKey);
			}
			
			mSearchTextBoxField = typeof(SearchForm).GetField("m_tbSearch", BindingFlags.Instance | BindingFlags.NonPublic);

			mChromeAccessibility = new ChromeAccessibilityWinEventHook();

			return true; // Initialization successful
		}

		private void mOptionsMenu_Click(object sender, EventArgs e)
		{
			using (var options = new Options(m_host))
			{
				options.MatchUrlField = MatchUrlField;
				options.CreateEntryHotKey = CreateEntryHotKey;
				options.CreateEntryTargetGroup = CreateEntryTargetGroup;
				options.AutoSkipUserName = AutoSkipUserName;
				options.ShowRepeatedSearch = ShowRepeatedSearch;

				if (options.ShowDialog(m_host.MainWindow) == DialogResult.OK)
				{
					MatchUrlField = options.MatchUrlField;
					CreateEntryHotKey = options.CreateEntryHotKey;
					CreateEntryTargetGroup = options.CreateEntryTargetGroup;
					AutoSkipUserName = options.AutoSkipUserName;
					ShowRepeatedSearch = options.ShowRepeatedSearch;

					// Unregister the old hotkey, and register the new
					if (mCreateEntryHotkeyId != 0)
					{
						HotKeyManager.UnregisterHotKey(mCreateEntryHotkeyId);
						mCreateEntryHotkeyId = 0;
					}
					if (CreateEntryHotKey != Keys.None)
					{
						mCreateEntryHotkeyId = HotKeyManager.RegisterHotKey(CreateEntryHotKey);
					}
				}
			}
		}

		#region Options
		private bool MatchUrlField
		{
			get { return m_host.CustomConfig.GetBool(OptionsConfigRoot + "MatchUrlField", true); }
			set { m_host.CustomConfig.SetBool(OptionsConfigRoot + "MatchUrlField", value); }
		}

		private Keys CreateEntryHotKey
		{
			get { return (Keys)m_host.CustomConfig.GetULong(OptionsConfigRoot + "CreateEntryHotKey", (ulong)Keys.None); }
			set { m_host.CustomConfig.SetULong(OptionsConfigRoot + "CreateEntryHotKey", (ulong)value); }
		}

		private PwUuid CreateEntryTargetGroup
		{
			get 
			{ 
				var hexString = m_host.CustomConfig.GetString(OptionsConfigRoot + "CreateEntryTargetGroup", null);
				if (String.IsNullOrEmpty(hexString))
				{
					return null;
				}
				return new PwUuid(MemUtil.HexStringToByteArray(hexString));
			}
			set 
			{
				m_host.CustomConfig.SetString(OptionsConfigRoot + "CreateEntryTargetGroup", value == null ? "" : value.ToHexString()); 
			}
		}

		private bool AutoSkipUserName
		{
			get { return m_host.CustomConfig.GetBool(OptionsConfigRoot + "AutoSkipUserName", false); }
			set { m_host.CustomConfig.SetBool(OptionsConfigRoot + "AutoSkipUserName", value); }
		}

		private bool ShowRepeatedSearch
		{
			get { return m_host.CustomConfig.GetBool(OptionsConfigRoot + "ShowRepeatedSearch", false); }
			set { m_host.CustomConfig.SetBool(OptionsConfigRoot + "ShowRepeatedSearch", value); }
		}

		#endregion

		private bool mCreatingEntry = false;
		
		private void HotKeyManager_HotKeyPressed(object sender, HotKeyEventArgs e)
		{
			if (mCreatingEntry) return;
			mCreatingEntry = true;
			try
			{

				// Unlock, if required
				m_host.MainWindow.ProcessAppMessage((IntPtr)Program.AppMessage.Unlock, IntPtr.Zero);

				if (m_host.MainWindow.IsAtLeastOneFileOpen())
				{
					string selectedText, url, title;
					WebBrowserUrl.GetFocusedBrowserInfo(mChromeAccessibility, out selectedText, out url, out title);

					if (!String.IsNullOrEmpty(url))
					{
						// Use only the root part of the URL
						try
						{
							var uri = new Uri(url);
							url = uri.GetLeftPart(UriPartial.Authority) + "/";
						}
						catch (UriFormatException)
						{
							// Just use the url exactly as given
						}
					}

					// Logic adapted from EntryTemplates.CreateEntry
					var database = m_host.Database;
					var entry = new PwEntry(true, true);
					if (!String.IsNullOrEmpty(title)) entry.Strings.Set(PwDefs.TitleField, new ProtectedString(database.MemoryProtection.ProtectTitle, title));
					if (!String.IsNullOrEmpty(url)) entry.Strings.Set(PwDefs.UrlField, new ProtectedString(database.MemoryProtection.ProtectUrl, url));
					if (!String.IsNullOrEmpty(selectedText)) entry.Strings.Set(PwDefs.UserNameField, new ProtectedString(database.MemoryProtection.ProtectUserName, selectedText));

					// Generate a default password, the same as in MainForm.OnEntryAdd
					ProtectedString psAutoGen;
					PwGenerator.Generate(out psAutoGen, Program.Config.PasswordGenerator.AutoGeneratedPasswordsProfile, null, Program.PwGeneratorPool);
					psAutoGen = psAutoGen.WithProtection(database.MemoryProtection.ProtectPassword);
					entry.Strings.Set(PwDefs.PasswordField, psAutoGen);

					using (var entryForm = new PwEntryForm())
					{
						entryForm.InitEx(entry, PwEditMode.AddNewEntry, database, m_host.MainWindow.ClientIcons, false, true);

						if (ShowForegroundDialog(entryForm) == DialogResult.OK)
						{
							PwGroup group = database.RootGroup;
							if (CreateEntryTargetGroup != null)
							{
								group = database.RootGroup.FindGroup(CreateEntryTargetGroup, true) ?? database.RootGroup;
							}
							
							group.AddEntry(entry, true, true);
							m_host.MainWindow.UpdateUI(false, null, database.UINeedsIconUpdate, null, true, null, true);
						}
						else
						{
							m_host.MainWindow.UpdateUI(false, null, database.UINeedsIconUpdate, null, database.UINeedsIconUpdate, null, false);
						}
					}
				}
			}
			finally
			{
				mCreatingEntry = false;
			}
		}

		private void ShowSearchDialog(string searchText)
		{
			using (var searchForm = new SearchForm())
			{
				searchForm.InitEx(m_host.Database, m_host.Database.RootGroup);
				if (mSearchTextBoxField != null)
				{
					var searchTextBox = mSearchTextBoxField.GetValue(searchForm) as TextBox;
					if (searchTextBox != null)
					{
						searchTextBox.Text = searchText;
					}
				}

				if (ShowForegroundDialog(searchForm) == DialogResult.OK)
				{
					m_host.MainWindow.UpdateUI(false, null, false, null, true, searchForm.SearchResultsGroup, false);

					// Things that can't be done without reflection:
					//m_host.MainWindow.ShowSearchResultsStatusMessage();
					//m_host.MainWindow.SelectFirstEntryIfNoneSelected();
					//m_host.MainWindow.ResetDefaultFocus(m_host.MainWindow.m_lvEntries);

					m_host.MainWindow.EnsureVisibleForegroundWindow(true, true);
				}
			}
		}

		private DialogResult ShowForegroundDialog(Form form)
		{
			m_host.MainWindow.EnsureVisibleForegroundWindow(false, false);
			form.StartPosition = FormStartPosition.CenterScreen;
			if (m_host.MainWindow.IsTrayed())
			{
				form.ShowInTaskbar = true;
			}

			form.Shown += FormOnShown;
			return form.ShowDialog(m_host.MainWindow);
		}

		private void FormOnShown(object sender, EventArgs eventArgs)
		{
			var form = (Form)sender;
			form.Shown -= FormOnShown;
			form.Activate();
		}

		private void AutoType_SequenceQueriesBegin( object sender, SequenceQueriesEventArgs e )
		{
			bool passwordFieldFocussed = false;

			string sUrl = WebBrowserUrl.GetFocusedBrowserUrl(mChromeAccessibility, e.TargetWindowHandle, out passwordFieldFocussed);

			if ( !string.IsNullOrEmpty( sUrl ) )
			{
				List<string> lstStrings = new List<string>();
				lstStrings.Add( sUrl );

				// store all possible variants
				// for those browsers where there's no good way to get full URL found yet
				if ( !sUrl.StartsWith( "http://" ) && !sUrl.StartsWith( "https://" ) )
				{
					lstStrings.Add( "http://" + sUrl );
					lstStrings.Add( "https://" + sUrl );
				}
				else if ( sUrl.StartsWith( "http://" ) )
				{
					lstStrings.Add( sUrl.Substring( 7 ) );
				}
				else if ( sUrl.StartsWith( "https://" ) )
				{
					lstStrings.Add( sUrl.Substring( 8 ) );
				}
				lock ( m_dicStrings )
				{
					m_dicStrings[e.EventID] = lstStrings;
					mSkipUserNameForSequence[e.EventID] = passwordFieldFocussed && AutoSkipUserName;
				}

				// Ensure starting un-found.
				mFoundSequence.Remove(e.EventID);
			}
		}

		private void AutoType_SequenceQueriesEnd( object sender, SequenceQueriesEventArgs e )
		{
			lock ( m_dicStrings )
			{
				if (ShowRepeatedSearch)
				{
					string url = null;
					List<string> lstStrings;
					if (m_dicStrings.TryGetValue(e.EventID, out lstStrings))
					{
						url = lstStrings.First();
					}

					if (url != null)
					{
						if (!mFoundSequence.Remove(e.EventID))
						{
							// Unsuccessful autotype
							if (mUnfoundUrls.Remove(url))
							{
								// Second unsuccessful auto-type for the same URL, show the search window
								m_host.MainWindow.BeginInvoke(new Action(() => ShowSearchDialog(url)));
							}
							else
							{
								// First unsuccessful auto-type, record the URL
								mUnfoundUrls.Add(url);
							}
						}
						else
						{
							// Successful autotype
							mUnfoundUrls.Remove(url);
						}
					}
				}

				m_dicStrings.Remove(e.EventID);
			}
		}

		private void AutoType_SequenceQuery( object sender, SequenceQueryEventArgs e )
		{
			string entryAutoTypeSequence = e.Entry.GetAutoTypeSequence();

			List<string> lstStrings;
			lock ( m_dicStrings )
			{
				if ( !m_dicStrings.TryGetValue( e.EventID, out lstStrings ) )
				{
					return;
				}

				bool skipUserName = false;
				mSkipUserNameForSequence.TryGetValue(e.EventID, out skipUserName);

				if (skipUserName && entryAutoTypeSequence.StartsWith(UserNameAutoTypeSequenceStart, StrUtil.CaseIgnoreCmp))
				{
					entryAutoTypeSequence = entryAutoTypeSequence.Substring(UserNameAutoTypeSequenceStart.Length);
				}
			}

			var matchFound = false;
			foreach ( AutoTypeAssociation association in e.Entry.AutoType.Associations )
			{
				string strUrlSpec = association.WindowName;
				if ( strUrlSpec == null )
				{
					continue;
				}

				strUrlSpec = strUrlSpec.Trim();

				if ( !strUrlSpec.StartsWith( c_sUrlPrefix ) || strUrlSpec.Length <= c_sUrlPrefix.Length )
				{
					continue;
				}

				strUrlSpec = strUrlSpec.Substring( 7 );

				if ( strUrlSpec.Length > 0 )
				{
					strUrlSpec = SprEngine.Compile( strUrlSpec, new SprContext( e.Entry, e.Database, SprCompileFlags.All ) );
				}

				bool bRegex = strUrlSpec.StartsWith( @"//" ) && strUrlSpec.EndsWith( @"//" ) && ( strUrlSpec.Length > 4 );
				Regex objRegex = null;

				if ( bRegex )
				{
					try
					{
						objRegex = new Regex( strUrlSpec.Substring( 2, strUrlSpec.Length - 4 ), RegexOptions.IgnoreCase );
					}
					catch ( Exception )
					{
						bRegex = false;
					}
				}

				foreach ( string s in lstStrings )
				{
					if ( bRegex )
					{
						if ( objRegex.IsMatch( s ) )
						{
							e.AddSequence( string.IsNullOrEmpty( association.Sequence ) ? entryAutoTypeSequence : association.Sequence );
							matchFound = true;
							break;
						}
					}
					else if ( StrUtil.SimplePatternMatch( strUrlSpec, s, StrUtil.CaseIgnoreCmp ) )
					{
						e.AddSequence(string.IsNullOrEmpty(association.Sequence) ? entryAutoTypeSequence : association.Sequence);
						matchFound = true;
						break;
					}
				}
			}

			if (MatchUrlField)
			{
				var url = e.Entry.Strings.GetSafe(KeePassLib.PwDefs.UrlField).ReadString();
				if (!String.IsNullOrEmpty(url) && lstStrings.Any(s => s.StartsWith(url, StrUtil.CaseIgnoreCmp)))
				{
					e.AddSequence(entryAutoTypeSequence);
					matchFound = true;
				}
			}

			if (matchFound && ShowRepeatedSearch)
			{
				lock (m_dicStrings)
				{
					mFoundSequence.Add(e.EventID);
				}
			}
		}

		private void GlobalWindowManager_WindowAdded( object p_sender, GwmWindowEventArgs p_e )
		{
			EditAutoTypeItemForm fEditForm = p_e.Form as EditAutoTypeItemForm;
			if( fEditForm != null )
			{
				m_fEditForm = fEditForm;

				Label lblDescription = (Label) fEditForm.Controls.Find( "m_lblOpenHint", false ).First();
				Label lblLeft = (Label) fEditForm.Controls.Find( "m_lblTargetWindow", false ).First();
				ImageComboBoxEx cmb = (ImageComboBoxEx) fEditForm.Controls.Find( "m_cmbWindow", false ).First();

				m_sLblText = lblLeft.Text;

				// button to get current browser URL
				Button button = new Button();
				fEditForm.Controls.Add( button );
				button.DialogResult = DialogResult.None;
				button.Location = new Point( cmb.Location.X + cmb.Size.Width - button.Size.Width, lblDescription.Location.Y - 6 );
				button.Name = "m_btnGetUrl";
				button.Size = new Size( 75, 23 );
				button.TabIndex = 80;
				button.Text = "Get &Url";
				button.Visible = false;
				button.UseVisualStyleBackColor = true;
				button.Click += button_Click;

				// button to switch to URL mode
				button = new Button();
				fEditForm.Controls.Add( button );

				button.DialogResult = DialogResult.None;
				button.Location = new Point( lblLeft.Location.X, lblDescription.Location.Y );
				button.Name = "m_btnSwitchType";
				button.Size = new Size( 75, 23 );
				button.TabIndex = 81;
				button.Text = "Url";
				button.UseVisualStyleBackColor = true;
				button.Click += button_TypeClick;

				TextBox textbox = new TextBox();
				fEditForm.Controls.Add( textbox );
				textbox.Visible = false;
				textbox.Location = cmb.Location;
				textbox.Name = "m_textboxCustomUrl";
				textbox.Size = cmb.Size;
				textbox.TabIndex = 0;
				textbox.TextChanged += textbox_TextChanged;
				textbox.Tag = cmb;

				fEditForm.Shown += EditForm_Shown;
			}
		}

		private void textbox_TextChanged( object sender, EventArgs e )
		{
			TextBox textbox = (TextBox) sender;
			ImageComboBoxEx cmb = (ImageComboBoxEx) textbox.Tag;

			cmb.Text = c_sUrlPrefix + textbox.Text;
		}

		private void EditForm_Shown( object sender, EventArgs e )
		{
			EditAutoTypeItemForm fEditForm = sender as EditAutoTypeItemForm;

			if ( fEditForm != null )
			{
				fEditForm.SuspendLayout();
				Button buttonType = (Button) fEditForm.Controls.Find( "m_btnSwitchType", false ).First();
				Button buttonUrl = (Button) fEditForm.Controls.Find( "m_btnGetUrl", false ).First();
				TextBox textbox = (TextBox) fEditForm.Controls.Find( "m_textboxCustomUrl", false ).First();
				ImageComboBoxEx cmb = (ImageComboBoxEx) textbox.Tag;
				Label lblLeft = (Label) fEditForm.Controls.Find( "m_lblTargetWindow", false ).First();
				Label lblDescription = (Label) fEditForm.Controls.Find( "m_lblOpenHint", false ).First();
				//LinkLabel lnkLabel = (LinkLabel) fEditForm.Controls.Find( "m_lnkWildcardRegexHint", false ).First();

				

				if ( cmb.Text.StartsWith( c_sUrlPrefix ) )
				{
					textbox.Text = cmb.Text.Substring( c_sUrlPrefix.Length );
					textbox.Visible = true;
					cmb.Visible = false;
					buttonUrl.Visible = true;
					lblDescription.Visible = false;
					//lnkLabel.Visible = false;
					buttonType.Text = "Window";
					lblLeft.Text = "URL:";
				}
				else
				{
					textbox.Visible = false;
					cmb.Visible = true;
					buttonUrl.Visible = false;
					lblDescription.Visible = true;
					//lnkLabel.Visible = true;
					buttonType.Text = "URL";
					lblLeft.Text = m_sLblText;
				}
				fEditForm.ResumeLayout();
			}
		}


		// get the topmost browser URL
		void button_Click( object sender, EventArgs e )
		{
			IntPtr hWnd = NativeMethodsLocal.GetForegroundWindow();
			string sUrl = null;
			while( hWnd != IntPtr.Zero && string.IsNullOrEmpty( sUrl ) )
			{
				hWnd = NativeMethodsLocal.GetWindow( hWnd, NativeMethodsLocal.GW_HWNDNEXT );
				sUrl = WebBrowserUrl.GetBrowserUrl( hWnd );
			}

			( sender as Button ).Parent.Controls["m_textboxCustomUrl"].Text = sUrl;
		}

		void button_TypeClick( object sender, EventArgs e )
		{
			Button buttonType = sender as Button;

			if ( buttonType != null )
			{
				EditAutoTypeItemForm fEditForm = buttonType.Parent as EditAutoTypeItemForm;
				fEditForm.SuspendLayout();
				Button buttonUrl = (Button) fEditForm.Controls.Find( "m_btnGetUrl", false ).First();
				TextBox textbox = (TextBox) fEditForm.Controls.Find( "m_textboxCustomUrl", false ).First();
				ImageComboBoxEx cmb = (ImageComboBoxEx) textbox.Tag;
				Label lblLeft = (Label) fEditForm.Controls.Find( "m_lblTargetWindow", false ).First();
				Label lblDescription = (Label) fEditForm.Controls.Find( "m_lblOpenHint", false ).First();
				//LinkLabel lnkLabel = (LinkLabel) fEditForm.Controls.Find( "m_lnkWildcardRegexHint", false ).First();

				if ( cmb.Visible )
				{
					textbox.Text = cmb.Text;
					cmb.Text = c_sUrlPrefix + textbox.Text;
					textbox.Visible = true;
					cmb.Visible = false;
					buttonUrl.Visible = true;
					lblDescription.Visible = false;
					//lnkLabel.Visible = false;
					buttonType.Text = "Window";
					lblLeft.Text = "URL:";
				}
				else
				{
					cmb.Text = textbox.Text;
					textbox.Visible = false;
					cmb.Visible = true;
					buttonUrl.Visible = false;
					lblDescription.Visible = true;
					//lnkLabel.Visible = true;
					buttonType.Text = "URL";
					lblLeft.Text = m_sLblText;
				}
				fEditForm.ResumeLayout();
			}
		}

		/// <summary>
		///
		/// </summary>
		public override void Terminate()
		{
			GlobalWindowManager.WindowAdded -= GlobalWindowManager_WindowAdded;
			AutoType.SequenceQuery -= AutoType_SequenceQuery;
			AutoType.SequenceQueriesBegin -= AutoType_SequenceQueriesBegin;
			AutoType.SequenceQueriesEnd -= AutoType_SequenceQueriesEnd;

			if ( m_fEditForm != null )
			{
				m_fEditForm.Shown -= EditForm_Shown;

				if ( !m_fEditForm.IsDisposed )
				{
					// need locks here?

					Button buttonType = (Button) m_fEditForm.Controls.Find( "m_btnSwitchType", false ).First();
					Button buttonUrl = (Button) m_fEditForm.Controls.Find( "m_btnGetUrl", false ).First();
					TextBox textbox = (TextBox) m_fEditForm.Controls.Find( "m_textboxCustomUrl", false ).First();
					ImageComboBoxEx cmb = (ImageComboBoxEx) textbox.Tag;
					Label lblLeft = (Label) m_fEditForm.Controls.Find( "m_lblTargetWindow", false ).First();
					Label lblDescription = (Label) m_fEditForm.Controls.Find( "m_lblOpenHint", false ).First();

					bool bVisible = m_fEditForm.Visible;

					if ( bVisible )
					{
						m_fEditForm.SuspendLayout();
						if ( !cmb.Visible )
						{
							cmb.Text = c_sUrlPrefix + textbox.Text;
							lblLeft.Text = m_sLblText;
						}
					}

					// remove all custom controls
					m_fEditForm.Controls.Remove( buttonType );
					m_fEditForm.Controls.Remove( buttonUrl );
					m_fEditForm.Controls.Remove( textbox );
					
					// restore visible state of default controls
					cmb.Visible = true;
					lblDescription.Visible = true;

					if ( bVisible )
					{
						m_fEditForm.ResumeLayout();
					}
				}
			}

			if (mOptionsMenu != null)
			{
				m_host.MainWindow.ToolsMenu.DropDownItems.Remove(mOptionsMenu);

				mOptionsMenu = null;
			}

			if (mCreateEntryHotkeyId != 0)
			{
				var result = HotKeyManager.UnregisterHotKey(mCreateEntryHotkeyId);
				Debug.Assert(result);
				mCreateEntryHotkeyId = 0;
			}

			if (mChromeAccessibility != null)
			{
				mChromeAccessibility.Dispose();
				mChromeAccessibility = null;
			}
		}
	}

		

	// copy of some functions from KeePass private NativeMethods class
	internal static class NativeMethodsLocal
	{
		[DllImport( "User32.dll" )]
		internal static extern IntPtr GetForegroundWindow(); // Private, is wrapped

		[DllImport( "user32.dll", SetLastError = true )]
		internal static extern IntPtr GetWindow( IntPtr hWnd, uint uCmd );

		internal const uint GW_HWNDNEXT = 2;
	}
}
