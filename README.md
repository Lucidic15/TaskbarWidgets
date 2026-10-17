# Taskbar Snipper

A [Windhawk](https://windhawk.net) mod: **Print Screen**, or a screenshot button on the taskbar,
selects part of the screen with a toolbox right above it to draw, and add lines, arrows, boxes and
text in any color and thickness, then move and restyle them. Click outside the selection when you're done, and the screenshot is copied to the
clipboard, saved to a folder, or both, as set in the mod's settings.

The button links with the [Taskbar Audio Flipper](https://windhawk.net/mods/taskbar-audio-flipper)
and [Taskbar RDP Launcher](https://windhawk.net/mods/taskbar-rdp-launcher) widgets into one.

## Trying it

Until the mod is in Windhawk's catalog, install it from this file:

1. In Windhawk, click **Create a new mod**. The editor opens with an example mod.
2. Replace all of it with the contents of [`taskbar-snipper.wh.cpp`](taskbar-snipper.wh.cpp).
3. Click **Compile mod**, then **Exit editing mode**.

To see what the mod is doing, turn on **Enable logging** in the editor's sidebar and click **Show log
output**.

## The code

The mod is one C++ file, compiled by Windhawk with Clang in C++23 mode, 32-bit for Windhawk 1.7 and
64-bit for Windhawk 2.0. Its widget code (the taskbar window, menus, storage and the link between
the widgets) is the same as in the Taskbar Audio Flipper and Taskbar RDP Launcher mods, each keeping
the parts it uses, so a change there belongs in all three.

Each tool is one entry in the `kTools` list, with the kind of mark it draws: a class that draws,
measures, hit-tests, moves and copies itself. The overlay handles every mark the same way, so a new
tool is a new entry and its mark class, with nothing else to change.
