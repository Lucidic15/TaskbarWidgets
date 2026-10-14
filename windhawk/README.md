# RDP Launcher as a Windhawk mod

[`rdp-launcher.wh.cpp`](rdp-launcher.wh.cpp) is RDPLauncher as a [Windhawk](https://windhawk.net)
mod: the same taskbar widget, with its display buttons, display picker, computers and menu, run by
Windhawk instead of by RDPLauncher.exe. There's nothing else to download or keep running, and
Windhawk starts it with Windows.

It pairs with the [AudioFlipper mod](https://github.com/Lucidic15/AudioFlipper/tree/main/windhawk):
the two widgets can be linked into one, on either side, as the apps can. A mod also links with the
other app.

## Differences from the app

- **No tray icon**, so the widget is always shown. To remove it, turn the mod off in Windhawk.
- **No Start with Windows or Exit** in the menu: Windhawk starts the mod at sign-in, and turning the
  mod off closes it.
- **No command line**: shortcuts to `RDPLauncher.exe --connect … --displays …` keep working
  alongside the mod.

Everything else works the same, and the mod uses the app's settings file
(`%APPDATA%\RDPLauncher\settings.txt`), so computers, names and display buttons carry over both ways.

Use the app or the mod, not both. While RDPLauncher.exe is running, the mod waits and takes over when
the app exits; while the mod is running, the app closes as soon as it starts. When switching to the
mod, turn off the app's **Start with Windows** and choose **Exit**.

## Trying it

Until the mod is in Windhawk's catalog, install it from this file:

1. Install [Windhawk](https://windhawk.net) and open it.
2. Click **Create a new mod**. The first time, Windhawk asks to **Install development tools**, which
   it needs to compile mods, and restarts when they're installed; then click **Create a new mod**
   again.
3. The editor opens with an example mod. Replace all of it with the contents of
   [`rdp-launcher.wh.cpp`](rdp-launcher.wh.cpp) (on GitHub, open the file and use **Copy raw file**).
4. Click **Compile mod**. Once it's compiled, the widget appears on the taskbar.
5. Click **Exit editing mode**. The mod stays installed and starts with Windows like any other.

To see what the mod is doing, turn on **Enable logging** in the editor's sidebar and click **Show log
output**. Once the mod is in the catalog, remove this local copy (**Remove mod**) before installing
the published one.

## Publishing it

Windhawk's catalog is the [windhawk-mods](https://github.com/ramensoftware/windhawk-mods)
repository. To publish the mod, or later an update with a higher `@version`, open a pull request
there that adds or changes just `mods/rdp-launcher.wh.cpp`, a copy of this file. It has to come from
the GitHub account in the mod's `@github` line (Lucidic15). Windhawk's maintainers review each
submission before it appears in the catalog.

## The code

The mod is one C++ file, compiled by Windhawk with Clang in C++23 mode. Its widget code (the taskbar
window, popups, menus, text prompt and the link between the widgets) is the same as in the
AudioFlipper mod, so a change there belongs in both.
