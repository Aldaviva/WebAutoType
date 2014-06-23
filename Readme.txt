WebAutoType
===========
http://sourceforge.net/projects/webautotype


This is a plugin to KeePass <http://www.KeePass.info> to allow the AutoType functionality to
work with browser URLs as well as window titles. It uses UIA accessibility technology to 'read'
the browser window, and is therefore at this time only supported on Windows.

Features
 * Support for all major browsers: Firefox, Chrome, Internet Explorer, Opera
 * Create custom AutoType target URLs, or optionally use the standard URL field to match against
 * Create custom AutoType sequences for different URLs in the same entry
 * Automatically skip User Name part of AutoType sequence when starting in a password box
 * Optionally define a shortcut key to create a new entry, pre-populated with information from
    the current browser page
 * Optionally show the search window on the second attempt to AutoType for a page with no
    entry found.


Installation
------------
Place WebAutoType.plgx in your KeePass Plugins folder. A "WebAutoType Options" menu item will
be added to the KeePass "Tools" menu.

If KeePass shows a message box indicating that the plugin is not compatible, then please ensure
that you have the .NET Framework 3.5 installed. (KeePass will work without that version installed
but the compilation process to install this plugin doesn't work with certain 4.X versions of the
framework)


Uninstallation
--------------
Delete WebAutoType.plgx from your KeePass Plugins folder.


Google Chrome
-------------
If accessibility is not enabled in Chrome, then WebAutoType can still detect the URL, but more
advanced functionality such as automatically skipping User Name for password boxes can't be used.

Chrome may not automatically enable accessibility, depending on the version. For several versions
the automatic detection of applications requesting accessibility was broken (issue #342319). This
has now been partially fixed, however it will now only check when Chrome is launched. If KeePass
(with WebAutoType) is already running when Chrome is launched it will detect it and automatically
enable accessibility. Otherwise, it will not be. To force accessiblity to be enabled even before
KeePass is running, you can launch Chrome with the "--force-renderer-accessibility" flag on the
command line.


Opera 12 (Presto)
-----------------
Opera does not, by default, have any accessibility exposed at all. In order to use WebAutoType
with opera, you must first enable accessibility from Opera. To do this:

1. Visit this url from within Opera: opera:config#UserPrefs|EnableAccessibilitySupport
2. Check Enable Accessibility Support, which is disabled by default
3. Scroll down and hit the Save button
4. Restart Opera (the preference change only affects newly created tabs)


Pale Moon
---------
Pale Moon (and similar FireFox variants) have Accessibility deliberately disabled in an attempt
to improve performance. Therefore WebAutoType can not read the URL from them, and will not work
at all. It is not possible for WebAutoType to support browsers which do not expose accessibility
information.


Usage
-----
To enable AutoType matching against the URL field in your entries, click the "WebAutoType Options"
entry in your "Tools" menu, and check the "Use the URL field value for matching" checkbox. When
this option is selected, the value of the URL field will be checked against the start of the URL
in the browser window, so if you URL field states "https://www.example.com" then the browser URL
"https://www.example.com/login.php" would match against that.

To define alternative, or custom URLs to match against for an entry, use the AutoType tab on the
KeePass entry editing window. Click the "Add" button, then under in the Edit Auto-Type Item box
click the URL button, and enter the URL to match against. Here, you can also include wildcards
and regular expressions, just as you can for window titles, so if you want the same behaviour of
matching just the start of the URL, end it with a * character (to mean any further characters
are valid here).

For multi-page logins, you can use these additional auto-type entries with the URLs of each page,
and a custom keystroke sequence for each page.


Automatically skipping User Name
--------------------------------
To have the AutoType sequence automatically skip the User Name part when starting from a password
entry box, check the "Automatically skip user name for passwords" box in the "WebAutoType Options"
window. When this option is enabled, if the cursor is in a Password edit box when the AutoType
hot key is pressed, then if the entry's AutoType sequence starts with "{username}{tab}" then that
part is ignored. Note that this won't be done for explicitly definied custom sequences for
specific windows or URLs, just the sequence defined for the entry, or the one it inherits from
its group.


Creating new Entries
--------------------
WebAutoType also offers the ability to set a shortcut for creating a new entry. To do this, click
the "WebAutoType Options" entry in your "Tools" menu, and enter a keyboard shortcut in the Global
hot key box. You may also select the group into which the new entry should be added. When the
hot key is pressed, a new entry will be created pre-populated with the following information:

Url: The root part of the URL of the current web page
Title: The title of the current web page.
User name: The contents of the textbox with the focus, if any. (usefull if your username is already
           entered in the form)
           
Currently, Title and User name are only supported on Firefox and Chrome (as long as accessibility
has been turned on - see the Chrome section for details) - other browsers will still populate
the URL, but the other information is not accessible and will be left blank.


Searching for Entries
---------------------
WebAutoType offers the ability to search for an entry. To enable this functionality, click the
"WebAutoType Options" entry in your "Tools" menu, and check the "Show search for repeated autotype"
box. Once enabled, if you trigger an AutoType for a web page, but no AutoType is performed (as
no matching entry for the URL was found), then simply trigger the AutoType for the same page a
second time (hit the same shortcut key again) and if it was still unable to find an AutoType match,
the Search window will be shown.

This is useful if you think that there should already be an entry for the page, but perhaps the URL
didn't match exactly or the entry might have AutoType disabled, or be in a group with AutoType
disabled.

The search text is pre-populated with the detected URL for the page.


Security
--------
WebAutoType does not expose access to your KeePass database in any way. It only extracts additional
data from the browser, using standard interfaces designed for screen readers and automation tools.

Password data is still transferred by KeePass, using Autotype, and the mechanism for this transfer
is not altered at all by WebAutoType.


Checking for updates
--------------------
If you want to use the KeePass Check for Updates function to check for updates to this plugin
then it requires the SourceForgeUpdateChecker plugin to be installed too:
http://sourceforge.net/projects/kpsfupdatechecker


Credits
-------
WebAutoType was initially developed by CEPOCTb. With his permission, version 3.0 has been released
as a derived project by Alex Vallat.


Bug Reporting, Questions, Comments, Feedback
--------------------------------------------
Please use the SourceForge project page: <http://sourceforge.net/projects/webautotype>
Bugs can be reported using the issue tracker, for anything else, a discussion forum is available.


Changelog
---------
v3.5
 Re-enabled support for Chrome accessibility (see above section on Chrome for further details)
 Added support for update checking, using SourceForgeUpdateChecker

v3.4
 Fixed crash if the options window was shown with no database loaded
 Added support for Chrome accessibility probing, so chrome should now automatically expose
  accessibility when KeePass with WebAutoType is running.

v3.3
 Added option for showing the Search window if AutoType is invoked twice for the same URL,
  unsuccessfully.
 Improved support for internationalised versions of Chrome

v3.2
 Fixed support for Chrome v29
 Improved reliability of UIA field detection after focus shift (for example, after using the unlock
  dialog in response to global autotype)
 When KeePass is minimized to the tray, the Add Entry dialog launched by the hotkey will now be
  given a taskbar button and brought to the front.

v3.1
 Added support for automatically skipping the UserName part AutoType sequences when starting from
  a password entry box.

v3.0
 Added support for URL field matching
 Added Create Entry hot key
 
v2.1.9
 Initial release by CEPOCTb
 <http://sourceforge.net/u/cepoctb/webautotype/>