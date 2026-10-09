# AudioFlipper

A small Windows 11 app for switching audio outputs from the taskbar. It adds a widget to the
taskbar, in the same style as the Windows weather widget, that always shows the active output
device's icon, its name and the volume. Click it to switch to the next output you have enabled
(speakers → headset → monitor → …).

**On the taskbar widget:**

- **Hover**: a flyout opens above it with the device's name, a volume slider, and the device icon
  as a mute button, like the Windows 11 volume flyout. It closes as soon as the mouse leaves it.
- **Left-click**: switch to the next output device.
- **Scroll**: turn the volume up or down.
- **Middle-click**: mute / unmute.
- **Right-click**: menu (see below).
- **Drag** left or right: move it along the taskbar. It remembers where you put it.

AudioFlipper also has a notification-area (tray) icon showing the active device's icon, with the
same clicks, so you can turn the widget off and use only the icon if you prefer.

The right-click menu lets you pick a device directly, choose which devices are in the click
rotation, open the Windows sound settings, and turn **Show on taskbar**, **Start with Windows**
and the other options on or off.

## Device names and icons

AudioFlipper shows each device with the name and icon it has in the Windows Sound control panel,
the same ones you see on its **Playback** tab.

To change them, choose **Rename or change device icons…** from the right-click menu. That opens the
Sound control panel's Playback tab: right-click a device, choose **Properties**, then type a new
name or click **Change Icon**. AudioFlipper picks up the change within a second.

## How the taskbar widget works

Windows 11 has no supported way for apps to add their own widgets to the taskbar: the old taskbar
toolbars ("deskbands") were removed, and the weather widget is built into Windows. So AudioFlipper's
widget is a transparent, always-on-top window that sits on the taskbar and looks like part of it.
It:

- keeps itself above the taskbar when you click the taskbar,
- hides while a full-screen app (game, video, presentation) is in front, and when an auto-hiding
  taskbar slides away,
- appears on the main taskbar only (not the taskbars on other monitors).

It starts just to the right of the weather widget. Drag it wherever you like; if you'd rather have
it in the weather widget's spot, turn off **Widgets** under **Settings → Personalization →
Taskbar** and drag it into the corner. It covers whatever taskbar space it's on, so keep it clear
of taskbar buttons.

Windows 11 tucks new tray icons into the `^` overflow menu. To show AudioFlipper's tray icon too,
drag it out of the overflow menu, or turn it on under **Settings → Personalization → Taskbar →
Other system tray icons**.

## Getting it

AudioFlipper targets .NET Framework 4.8, which is part of Windows 10 and 11, so there's nothing
else to install.

- **Download:** open the repository's **Actions** tab, select the latest **Build** run, and
  download the `AudioFlipper` artifact. Extract the zip, put `AudioFlipper.exe` somewhere it can
  stay (for example a folder in Documents) and double-click it. Windows SmartScreen may warn about
  an unsigned app; choose **More info → Run anyway**.
- **Build it yourself:** install the [.NET SDK](https://dotnet.microsoft.com/download) and run

  ```
  dotnet build src/AudioFlipper/AudioFlipper.csproj -c Release
  ```

  The exe is written to `src/AudioFlipper/bin/Release/net48/AudioFlipper.exe`.

Use **Start with Windows** in the right-click menu to launch it at sign-in, and **Exit** to close it.

## Which devices are in the rotation

The rotation is every output that is enabled and connected (what Windows calls *active*), in
alphabetical order. Devices that are disabled in Windows, or unplugged, are skipped automatically.
To leave out an output you still want enabled (an HDMI monitor's speakers, for example), untick it
under **Include in click rotation** in the right-click menu.

By default switching also changes the *communications* device (used by Teams, Discord, etc.) so
everything plays from the same output. Untick **Also switch communications device** to leave it
alone.

When the taskbar widget is turned off (or hidden by a full-screen app), a small popup above the
taskbar names the new device after each switch. Turn it off with **Show popup on change**.

## Command line

`AudioFlipper.exe --next` switches to the next device and exits, without a tray icon. You can use
it to get a keyboard shortcut: create a shortcut to `AudioFlipper.exe --next` on the desktop or in
the Start menu, open its **Properties**, and set a **Shortcut key**. If AudioFlipper is running,
its widget and icon update to show the new device.

## How it works

- Devices, their names and icons, and volume are read with the Windows Core Audio API
  (`IMMDeviceEnumerator`, `IAudioEndpointVolume`).
- Windows has no public API for changing the default output. AudioFlipper uses the undocumented
  `IPolicyConfig` interface that the Sound control panel itself uses; it has been stable since
  Windows 7 and is what other switchers such as EarTrumpet and SoundSwitch rely on.
- Settings are stored in `%APPDATA%\AudioFlipper\settings.txt`.
