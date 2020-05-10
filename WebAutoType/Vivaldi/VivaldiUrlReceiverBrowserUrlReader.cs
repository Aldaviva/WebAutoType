#nullable enable

namespace WebAutoType.Vivaldi
{
	public class VivaldiUrlReceiverBrowserUrlReader : IBrowserUrlReader
	{
		internal VivaldiUrlReceiver? UrlReceiver { get; set; }

		public string? GetWindowUrl()
		{
			return UrlReceiver?.MostRecentReceivedUrl?.ToString();
		}

		public string? GetBrowserFocusUrl(out bool passwordFieldFocussed)
		{
			passwordFieldFocussed = false;
			return GetWindowUrl();
		}

		public string? GetBrowserFocusUrlWithInfo(out string? title, out string? selectedText)
		{
			title = null;
			selectedText = null;
			return GetWindowUrl();
		}
	}
}