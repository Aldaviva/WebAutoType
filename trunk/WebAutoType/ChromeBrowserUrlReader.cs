using System;
using System.Linq;
using System.Windows.Forms;
using Accessibility;

namespace WebAutoType
{
	internal class ChromeBrowserUrlReader : BrowserUrlReader
	{
		// When Chrome enables accessibility, it takes a little while to enable. This value controls how long we wait to try again.
		private static readonly TimeSpan ChromeRePollDelay = TimeSpan.FromMilliseconds(500);

		public ChromeBrowserUrlReader(IntPtr hwnd) : base(hwnd)
		{
		}

		protected override IAccessible GetDocument()
		{
			var chromeRenderHwnd = FindDescendantWindows(mHwnd, "Chrome_RenderWidgetHostHWND").FirstOrDefault();
			if (chromeRenderHwnd == IntPtr.Zero)
			{
				return null;
			}

			// Chrome only enables accessibility if it gets a top-level IAccessible request, so let's make one first
			var _ = AccessibleObjectHelper.GetAccessibleObjectFromWindow(mHwnd).accName;

			return AccessibleObjectHelper.FindChild(AccessibleObjectHelper.GetAccessibleObjectFromWindow(chromeRenderHwnd),
				role: AccessibleRole.Document);
		}

		public ChromeAccessibilityWinEventHook ChromeAccessibilityWinEventHook;

		public override string GetBrowserFocusUrl(out bool passwordFieldFocussed)
		{
			if (ChromeAccessibilityWinEventHook != null)
			{
				ChromeAccessibilityWinEventHook.EventReceived = false;
			}
			var url = base.GetBrowserFocusUrl(out passwordFieldFocussed);
			if (ChromeAccessibilityWinEventHook != null && ChromeAccessibilityWinEventHook.EventReceived)
			{
				// If Chrome accessibility received an event, then Chrome has just turned on accessibility, so re-query for the focused element now that Chrome will actually provide it.
				System.Threading.Thread.Sleep(ChromeRePollDelay);
				url = base.GetBrowserFocusUrl(out passwordFieldFocussed);
			}
			return url;
		}
	}
}