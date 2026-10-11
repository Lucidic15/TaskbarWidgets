# RDPLauncher

A small Windows 11 app that starts Remote Desktop on as many of your displays as you want, from
the taskbar. Instead of editing `use multimon` and `selectedmonitors` in an .rdp file (after
looking up the display numbers with `mstsc /l`), click a number.

It adds a widget to the taskbar, in the same style as the Windows weather widget (and as
[AudioFlipper](https://github.com/Lucidic15/AudioFlipper)): a Remote Desktop icon with the
computer's name and address, then a row of numbered display buttons.

- **Number buttons**: click **1**, **2** or **3** to connect full screen on that many displays. There's
  one button for each display you have.
- **Hover a number**: a flyout opens above it with a map of your displays, laid out the way they are
  on your desk. The ones that button uses are highlighted, and the session's main display (where
  the remote taskbar goes) has a bar along its bottom. **Click a display to add it or take it off.**
- **The computer's name**: click it to pick another computer or add one. Scroll over it to go
  through your computers.

Anywhere on the widget, **right-click** for the menu and **drag** left or right to move it along the
taskbar. It remembers where you put it.

## Which displays each button uses

Out of the box, **1** is your main display, **2** adds the display nearest to it, and so on up to
all of them. If you'd rather **2** were your left and right displays, hover **2** and click the
displays you want. The buttons are remembered for each arrangement of displays, so a laptop that's
sometimes docked keeps separate buttons for the desk and for the road. **Reset display buttons** in
the right-click menu goes back to the defaults.

The session's main display is your main display if it's one of the chosen ones, otherwise the
leftmost chosen display.

## Computers

Add computers from the **Computers** submenu of the right-click menu (or by clicking the name on
the widget):

- **Add a computer…**: type its name or address, as you would in Remote Desktop Connection. It
  connects with the settings Remote Desktop Connection itself uses (its `Default.rdp`), and the
  user name it remembers for that computer. **Default Remote Desktop settings…** in the menu opens
  those settings (shared by every computer added by name) to change sound, drives, the clipboard
  and so on.
- **Add a saved .rdp file…**: for a computer with its own settings (a gateway, for example).
  **Edit *name*.rdp…** in the menu opens it in Remote Desktop Connection.
- **Add a recent computer**: the computers you've connected to with Remote Desktop Connection.

**Connect to *name*** in the menu also has **In a window**, for a session that isn't full screen.

## How it works

RDPLauncher never changes your .rdp files. When you click a button it writes a copy to
`%LOCALAPPDATA%\RDPLauncher\Sessions\<name>.rdp`, with the display settings set:

```
screen mode id:i:2
use multimon:i:1
span monitors:i:0
selectedmonitors:s:1,0
```

and opens that with Remote Desktop Connection (`mstsc.exe`). With all displays chosen it leaves out
`selectedmonitors`, which means all of them.

The numbers in `selectedmonitors` are Remote Desktop's own display IDs, which aren't the numbers
Windows Settings shows and can change when displays are plugged in or a dock is swapped. So each
time it connects on some of your displays, RDPLauncher runs `mstsc /l` (which lists them), reads the
list and closes it straight away; you may see its box flash. It matches the list to your displays by
their position. If Remote Desktop Connection doesn't list them, RDPLauncher guesses from the order
Windows lists the displays in, and says so.

If an .rdp file is signed (for example by your IT department) and its signature covers the display
settings, the copy leaves the signature out, since changing those settings breaks it. Remote
Desktop Connection then asks you to confirm the connection, as for any unsigned file.

## Good to know

- The first time you connect to each computer, Remote Desktop Connection may say it can't identify
  the publisher (RDPLauncher connects through an .rdp file, which typing the name in doesn't). Tick **Don't ask me again for connections to this computer** and
  choose **Connect**.
- Pick displays that are next to each other. Remote Desktop can have trouble with a gap between
  the displays it uses.
- Using more than one display needs the remote computer to support it: Windows Pro, Enterprise or
  Server do, Windows Home can't be connected to at all.

## How the taskbar widget works

Windows 11 has no supported way for apps to add their own widgets to the taskbar: the old taskbar
toolbars ("deskbands") were removed, and the weather widget is built into Windows. So RDPLauncher's
widget is a transparent, always-on-top window that sits on the taskbar and looks like part of it.
It:

- keeps itself above the taskbar when you click the taskbar,
- hides while a full-screen app (a game, a video, or a full-screen Remote Desktop session) is in
  front, and when an auto-hiding taskbar slides away,
- appears on the main taskbar only (not the taskbars on other monitors).

It starts a little to the right of the weather widget, leaving room for AudioFlipper's widget
between them. Drag it wherever you like; if you'd rather have it in the weather widget's spot, turn
off **Widgets** under **Settings → Personalization → Taskbar** and drag it into the corner. It
covers whatever taskbar space it's on, so keep it clear of taskbar buttons.

RDPLauncher also has a notification-area (tray) icon; click it for the same menu. Turn the widget
off with **Show on taskbar** and the tray icon remains. Windows 11 tucks new tray icons into the `^`
overflow menu; drag it out, or turn it on under **Settings → Personalization → Taskbar → Other
system tray icons**.

## Getting it

RDPLauncher targets .NET Framework 4.8, which is part of Windows 10 and 11, so there's nothing else
to install.

- **Download:** get `RDPLauncher.exe` from the latest release on the repository's
  [Releases](https://github.com/Lucidic15/RDPLauncher/releases/latest) page. Put it somewhere it
  can stay (for example a folder in Documents) and double-click it. Windows SmartScreen may warn
  about an unsigned app; choose **More info → Run anyway**. Builds of every change are also on the
  **Actions** tab.
- **Build it yourself:** install the [.NET SDK](https://dotnet.microsoft.com/download) and run

  ```
  dotnet build src/RDPLauncher/RDPLauncher.csproj -c Release
  ```

  The exe is written to `src/RDPLauncher/bin/Release/net48/RDPLauncher.exe`.

Use **Start with Windows** in the right-click menu to launch it at sign-in, and **Exit** to close it.

To publish a new release, set `<Version>` in `src/RDPLauncher/RDPLauncher.csproj` and add release
notes as `.github/release-notes/v<version>.md`. Then on the **Actions** tab, choose **Release →
Run workflow**, pick the branch and enter the version (or push a `v<version>` tag). The workflow
builds the exe, creates the tag and publishes the release with the exe attached.

## Command line

```
RDPLauncher.exe --connect <computer> --displays <number | all | window>
```

starts Remote Desktop and exits, without the widget. `--connect` takes a computer from your list
(by the name the widget shows) or any computer name or .rdp file, and defaults to the computer
picked on the widget. `--displays` uses the widget's button for that many displays, and defaults to
`all`. For example `RDPLauncher.exe --connect workpc --displays 2`.

Use it for keyboard shortcuts: create a shortcut to it on the desktop or in the Start menu, open its
**Properties**, and set a **Shortcut key**.

## Settings

Settings are stored in `%APPDATA%\RDPLauncher\settings.txt`.
