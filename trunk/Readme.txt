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
 * Optionally define a shortcut key to create a new entry, pre-populated with information from
    the current browser page

Installation
------------
Place WebAutoType.plgx in your KeePass Plugins folder. A "WebAutoType Options" menu item will
be added to the KeePass "Tools" menu.

Uninstallation
--------------
Delete WebAutoType.plgx from your KeePass Plugins folder.

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

WebAutoType also offers the ability to set a shortcut for creating a new entry. To do this, click
the "WebAutoType Options" entry in your "Tools" menu, and enter a keyboard shortcut in the Global
hot key box. You may also select the group into which the new entry should be added. When the
hot key is pressed, a new entry will be created pre-populated with the following information:

Url: The root part of the URL of the current web page
Title: The title of the current web page.
User name: The contents of the textbox with the focus, if any. (usefull if your username is already
           entered in the form)
           
Currently, Title and User name are only supported on Firefox - other browsers will still populate
the URL, but the other information is not accessible and will be left blank.


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
v3.0
 Added support for URL field matching
 Added Create Entry hot key
 
v2.1.9
 Initial release by CEPOCTb
 <http://sourceforge.net/u/cepoctb/webautotype/>