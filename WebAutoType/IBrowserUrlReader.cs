namespace WebAutoType
{
	/// <summary>
	/// The external contract of the BrowserUrlReader classes which the rest of this project depends on.
	/// </summary>
	internal interface IBrowserUrlReader
	{

		/// <summary>
		/// Gets the URL of the top level browser window, ignoring keyboard focus
		/// </summary>
		string GetWindowUrl();

		/// <summary>
		/// Gets the URL of the frame of the browser that has the focus, and the focussed element
		/// </summary>
		string GetBrowserFocusUrl(out bool passwordFieldFocussed);

		string GetBrowserFocusUrlWithInfo(out string title, out string selectedText);

	}

}