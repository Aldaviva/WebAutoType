using System;
using System.Diagnostics;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using WebAutoType.Vivaldi;

namespace WebAutoType
{
	/// <summary>
	/// Class to retrive URL from specified window (if it's a browser window)
	/// </summary>
	public static class WebBrowserUrl
	{
		[DllImport("user32.dll")]
		private static extern IntPtr GetForegroundWindow();

		[DllImport("user32.dll")]
		private static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool IsWindowVisible(IntPtr hWnd);

		private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

		/// <summary>Temporary holder during enumeration</summary>
		private static List<IntPtr> sTopLevelBrowserWindowHandles;

		/// <summary>
		/// Gets URLs for all top-level supported browser windows
		/// </summary>
		/// <returns></returns>
		public static IEnumerable<String> GetTopLevelBrowserWindowUrls(BrowserHelpers browserHelpers)
		{
			sTopLevelBrowserWindowHandles = new List<IntPtr>();
			EnumWindows(EnumWindows, IntPtr.Zero);
			var urls = new List<String>(sTopLevelBrowserWindowHandles.Count);
			foreach (var hwnd in sTopLevelBrowserWindowHandles)
			{
				var windowUrl = CreateBrowserUrlReader(hwnd, browserHelpers).GetWindowUrl();
				if (!String.IsNullOrEmpty(windowUrl))
				{
					urls.Add(windowUrl);
				}
			}
			return urls;
		}

		private static bool EnumWindows(IntPtr hWnd, IntPtr lParam)
		{
			if (IsWindowVisible(hWnd) && BrowserUrlReader.IsWindowHandleSupportedBrowser(hWnd))
			{
				sTopLevelBrowserWindowHandles.Add(hWnd);
			}
			return true;
		}

		/// <summary>
		/// Gets the URL from the browser with the current focus. If there is no current focus, falls back on trying to get the active URL from
		/// the fallback top-level window handle specified.
		/// 
		/// If the current focus is detected to be in a password field, passwordFieldFocussed is set true.
		/// </summary>
		internal static string GetFocusedBrowserUrl(BrowserHelpers browserHelpers, IntPtr hwnd, out bool passwordFieldFocussed)
		{
			var browserUrlReader = CreateBrowserUrlReader(hwnd, browserHelpers);

			return browserUrlReader.GetBrowserFocusUrl(out passwordFieldFocussed);
		}

		internal static void GetFocusedBrowserInfo(BrowserHelpers browserHelpers, out string selectedText, out string url, out string title)
		{
			var browserUrlReader = CreateBrowserUrlReader(GetForegroundWindow(), browserHelpers);

			if (browserUrlReader == null)
			{
				selectedText = null;
				url = null;
				title = null;
			}
			else
			{
				url = browserUrlReader.GetBrowserFocusUrlWithInfo(out title, out selectedText);
			}
		}

		/// <summary>
		/// Unify creation of IBrowserUrlReaders. Also injects dependencies, which is manual since this project doesn't have DI.
		/// </summary>
		/// <param name="hWnd"></param>
		/// <param name="browserHelpers"></param>
		/// <returns></returns>
		private static IBrowserUrlReader CreateBrowserUrlReader(IntPtr hWnd, BrowserHelpers browserHelpers)
		{
			IBrowserUrlReader urlReader = BrowserUrlReader.Create(hWnd);
			switch (urlReader)
			{
				case ChromeBrowserUrlReader chromeBrowserUrlReader:
					chromeBrowserUrlReader.ChromeAccessibilityWinEventHook = browserHelpers.ChromeAccessibilityWinEventHook;
					break;
				case VivaldiUrlReceiverBrowserUrlReader vivaldiHttpBrowserUrlReader:
					vivaldiHttpBrowserUrlReader.UrlReceiver = browserHelpers.VivaldiUrlReceiver;
					break;
			}

			return urlReader;
		}
	}
}
