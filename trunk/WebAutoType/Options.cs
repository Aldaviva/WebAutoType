using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using KeePass.UI;
using KeePassLib.Native;
using KeePassLib;
using KeePass.Plugins;

namespace WebAutoType
{
	public partial class Options : Form
	{
		private readonly HotKeyControlEx mCreateEntryShortcutKey;
		private readonly IPluginHost mHost;

		public Options()
		{
			InitializeComponent();
		}

		public Options(IPluginHost host) : this()
		{
			mHost = host;

			mCreateEntryShortcutKey = HotKeyControlEx.ReplaceTextBox(mCreateEntryGroupBox, mCreateEntryShortcutKeyTextBox, false);
			mCreateEntryShortcutKey.TextChanged += mCreateEntryShortcutKey_TextChanged;
			if (NativeLib.IsUnix())
			{
				mCreateEntryShortcutKey.Enabled = false;
				mCreateEntryShortcutKey.Clear();
				mCreateEntryShortcutKey.RenderHotKey();
			}

			AddGroupToCombo(mHost.Database.RootGroup, 0);

			// Set initial UI state
			mCreateEntryShortcutKey_TextChanged(null, EventArgs.Empty);
		}

		public bool MatchUrlField
		{
			get { return mMatchURLField.Checked; }
			set { mMatchURLField.Checked = value; }
		}

		public Keys CreateEntryHotKey
		{
			get { return mCreateEntryShortcutKey.HotKey | mCreateEntryShortcutKey.HotKeyModifiers; }
			set 
			{
				mCreateEntryShortcutKey.HotKey = value & Keys.KeyCode;
				mCreateEntryShortcutKey.HotKeyModifiers = value & Keys.Modifiers;

				mCreateEntryShortcutKey.RenderHotKey();
			}
		}

		public PwUuid CreateEntryTargetGroup
		{
			get
			{
				var selectedItem = mTargetGroup.SelectedItem as GroupComboItem;
				if (selectedItem == null)
				{
					return null;
				}
				return selectedItem.Uuid;
			}

			set
			{
				if (value == null)
				{
					mTargetGroup.SelectedIndex = -1;
				}
				else
				{
					mTargetGroup.SelectedItem = mTargetGroup.Items.OfType<GroupComboItem>().FirstOrDefault(item => item.Uuid.EqualsValue(value));
				}
			}
		}

		/// <summary>
		/// Recursively adds a group and all its child groups to the combo
		/// </summary>
		private void AddGroupToCombo(PwGroup group, int indentLevel)
		{
			mTargetGroup.Items.Add(new GroupComboItem(mHost, indentLevel, group));
			foreach (var child in group.GetGroups(false))
			{
				AddGroupToCombo(child, indentLevel + 1);
			}
		}
		
		protected override void OnValidating(CancelEventArgs e)
		{
			mCreateEntryShortcutKey.ResetIfModifierOnly();
			base.OnValidating(e);
		}

		private void mCreateEntryShortcutKey_TextChanged(object sender, EventArgs e)
		{
			if (mCreateEntryShortcutKey.HotKey != Keys.None)
			{
				mTargetGroupLabel.Enabled = mTargetGroup.Enabled = true;
				mTargetGroup.SelectedIndex = 0;
			}
			else
			{

				mTargetGroupLabel.Enabled = mTargetGroup.Enabled = false;
				mTargetGroup.SelectedIndex = -1;
			}
		}

		private void mTargetGroup_DrawItem(object sender, DrawItemEventArgs e)
		{
			if (e.Index >= 0)
			{
				var item = mTargetGroup.Items[e.Index] as GroupComboItem;
				if (item != null)
				{
					item.Draw(e);
				}
			}
		}

		private class GroupComboItem
		{
			private readonly int mIndentPosition;
			private readonly int mTextPosition;
			private readonly Image mImage;
			private readonly string mText;
			private readonly PwUuid mUuid;

			public GroupComboItem(IPluginHost host, int indentLevel, PwGroup group)
			{
				if (group.CustomIconUuid != PwUuid.Zero)
				{
					mImage = host.Database.GetCustomIcon(group.CustomIconUuid);
				}
				else
				{
					mImage = host.MainWindow.ClientIcons.Images[(int)group.IconId];
				}

				mIndentPosition = indentLevel * mImage.Width;
				mTextPosition = mIndentPosition + mImage.Width + 2;

				mText = group.Name;

				mUuid = group.Uuid;
			}

			public PwUuid Uuid { get { return mUuid; } }

			public void Draw(DrawItemEventArgs e)
			{
				e.DrawBackground();
				e.Graphics.DrawImage(mImage, e.Bounds.X + mIndentPosition, e.Bounds.Y, mImage.Width, mImage.Height);

				var textArea = new Rectangle(e.Bounds.X + mTextPosition, e.Bounds.Y, 
											 e.Bounds.Width - mTextPosition, e.Bounds.Height);

				TextFormatFlags tff = (TextFormatFlags.PreserveGraphicsClipping |
					TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix |
					TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter);
				
				TextRenderer.DrawText(e.Graphics, mText, e.Font, textArea, e.ForeColor, e.BackColor, tff);

				if (((e.State & DrawItemState.Focus) != DrawItemState.None) &&
					((e.State & DrawItemState.NoFocusRect) == DrawItemState.None))
					e.DrawFocusRectangle();
			}
		}
	}
}
