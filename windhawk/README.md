# Taskbar Audio Flipper, the Windhawk mod

[`taskbar-audio-flipper.wh.cpp`](taskbar-audio-flipper.wh.cpp) is AudioFlipper as a
[Windhawk](https://windhawk.net) mod: the same taskbar widget, with its Output, Input and Webcam sections, volume flyout and menu, run
by Windhawk instead of by AudioFlipper.exe. There's nothing else to download or keep running, and
Windhawk starts it with Windows.

It pairs with the [Taskbar RDP Launcher mod](https://github.com/Lucidic15/RDPLauncher/tree/main/windhawk):
the two widgets can be linked into one, on either side, as the apps can. The mods link with each
other only, not with the apps.

## Differences from the app

- **No tray icon**, so the widget is always shown, with the Output or Input section (or both). To
  remove it, turn the mod off in Windhawk.
- **No Start with Windows or Exit** in the menu: Windhawk starts the mod at sign-in, and turning the
  mod off closes it.
- **No command line**: shortcuts to `AudioFlipper.exe --next` keep working alongside the mod.
- **Webcam switching**: the app's **Switch without asking each time** isn't in the mod, and the mod
  doesn't use the scheduled tasks the app sets up for it. Each switch runs `pnputil.exe` as
  administrator, which may ask for permission, depending on User Account Control's settings.
- **Icon style**: the mod shows Fluent glyphs in the taskbar's color for the output and input by
  default, and **Icon style → Windows device icons** in its menu switches to the devices' Windows
  icons, which the app always shows.
- **Its own settings**: the mod keeps its settings in Windhawk's storage for it, which Windhawk
  removes with the mod. The first time it runs, it imports the app's settings file
  (`%APPDATA%\AudioFlipper\settings.txt`, which it only reads), so the sections, rotation, webcam
  and its name carry over from the app to the mod, once.

Everything else works the same. Use the app or the mod, not both: they no longer know about each
other, so with both running there are two widgets. When switching to the mod, turn off the app's
**Start with Windows** and choose **Exit**.

## Trying it

Until the mod is in Windhawk's catalog, install it from this file:

1. Install [Windhawk](https://windhawk.net) and open it.
2. Click **Create a new mod**. The first time, Windhawk asks to **Install development tools**, which
   it needs to compile mods, and restarts when they're installed; then click **Create a new mod**
   again.
3. The editor opens with an example mod. Replace all of it with the contents of
   [`taskbar-audio-flipper.wh.cpp`](taskbar-audio-flipper.wh.cpp) (on GitHub, open the file and use
   **Copy raw file**).
4. Click **Compile mod**. Once it's compiled, the widget appears on the taskbar.
5. Click **Exit editing mode**. The mod stays installed and starts with Windows like any other.

To see what the mod is doing, turn on **Enable logging** in the editor's sidebar and click **Show log
output**. Once the mod is in the catalog, remove this local copy (**Remove mod**) before installing
the published one.

## Publishing it

Windhawk's catalog is the [windhawk-mods](https://github.com/ramensoftware/windhawk-mods)
repository. To publish the mod, or later an update with a higher `@version`, open a pull request
there that adds or changes just `mods/taskbar-audio-flipper.wh.cpp`, a copy of this file. It has to
come from the GitHub account in the mod's `@github` line (Lucidic15), and its description has to keep
the pull request template's **Mod authorship** section, filled in. Windhawk's maintainers review each
submission before it appears in the catalog.

## The code

The mod is one C++ file, compiled by Windhawk with Clang in C++23 mode, 32-bit for Windhawk 1.7
(which runs tool mods in its 32-bit `windhawk.exe`) and 64-bit for Windhawk 2.0. Its widget code (the
taskbar window, popups, menus, text prompt, storage and the link between the widgets) is the same as
in the Taskbar RDP Launcher mod, each keeping the parts it uses, so a change there belongs in both.
