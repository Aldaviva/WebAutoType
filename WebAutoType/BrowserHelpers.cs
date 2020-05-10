#nullable enable

using WebAutoType.Vivaldi;

namespace WebAutoType
{
	/// <summary>
	/// Dependencies of various IBrowserUrlReaders. No dependency injection in this project, so we have to pass these around manually.
	/// </summary>
	public readonly struct BrowserHelpers
	{
		public ChromeAccessibilityWinEventHook ChromeAccessibilityWinEventHook { get; }
		public VivaldiUrlReceiver VivaldiUrlReceiver { get; }

		public BrowserHelpers(ChromeAccessibilityWinEventHook chromeAccessibilityWinEventHook, VivaldiUrlReceiver vivaldiUrlReceiver)
		{
			ChromeAccessibilityWinEventHook = chromeAccessibilityWinEventHook;
			VivaldiUrlReceiver = vivaldiUrlReceiver;
		}
	}
}