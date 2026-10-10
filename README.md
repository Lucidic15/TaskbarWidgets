# AudioFlipper

A small Windows 11 app for switching audio devices from the taskbar. It adds a widget to the
taskbar, in the same style as the Windows weather widget, with up to three sections that you can
each show or hide:

- **Output**: the active output device's icon, name and volume. Click it to switch to the next
  output you have enabled (speakers → headset → monitor → …).
- **Input**: the same for your microphone.
- **Webcam**: a camera icon that turns a chosen webcam on and off (see below).

**On the Output and Input sections:**

- **Hover**: a flyout opens above it with the device's name, a volume slider, and the device icon
  as a mute button, like the Windows 11 volume flyout. It closes as soon as the mouse leaves it.
- **Left-click**: switch to the next device.
- **Scroll**: turn the volume (or microphone level) up or down.
- **Middle-click**: mute / unmute.

Anywhere on the widget, **right-click** for the menu and **drag** left or right to move it along the
taskbar. It remembers where you put it.

AudioFlipper also has a notification-area (tray) icon for the output, showing the active device's
icon, with the same clicks.

The right-click menu has an **Output**, an **Input** and a **Webcam** submenu. Each has a **Show on
taskbar** switch for its section of the widget, and the settings for it: picking a device directly,
which devices are in the click rotation, whether to switch the communications device too, and links
to the Windows settings. **Start with Windows** and **Exit** are at the bottom. With all three
sections turned off the widget is hidden and only the tray icon remains.

## Device names and icons

AudioFlipper shows each device with the name and icon it has in the Windows Sound control panel,
the same ones you see on its **Playback** and **Recording** tabs.

To change them, choose **Rename or change device icons…** from the **Output** or **Input** submenu.
That opens the Sound control panel on the matching tab: right-click a device, choose
**Properties**, then type a new name or click **Change Icon**. AudioFlipper picks up the change
within a second.

## Webcam on/off

AudioFlipper can also turn a webcam on and off from the taskbar widget. Turn it on under
**Webcam → Show on taskbar** in the right-click menu, and pick the webcam to control from the
list in the same menu.

The widget then gets a camera icon on its right, with a slash through it while the webcam is off.
Click it to switch. **Webcam → Camera settings…** opens the camera page in Windows Settings.

Turning the webcam off works like **Disable device** in Device Manager: no app can use it until it's
turned back on. Windows only lets administrators do that, so by default each switch shows a Windows
prompt asking to allow "PnP Utility" (Microsoft's `pnputil.exe`) to make changes.

To stop the prompts, choose **Webcam → Switch without asking each time**. That asks once, and sets
up two scheduled tasks ("AudioFlipper Webcam On" and "AudioFlipper Webcam Off") that run
`pnputil.exe` from the Windows folder to turn that one webcam on or off. AudioFlipper starts them
when you click the camera, with no prompt. They can't run anything else. Choosing the option again
removes them (and asks once more). Picking a different webcam sets them up for that one.

Webcams that are unplugged don't show on the widget.

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

- **Download:** get `AudioFlipper.exe` from the latest release on the repository's
  [Releases](https://github.com/Lucidic15/AudtioFlipper/releases/latest) page. Put it somewhere it can stay (for example a folder in
  Documents) and double-click it. Windows SmartScreen may warn about an unsigned app; choose
  **More info → Run anyway**. Builds of every change are also on the **Actions** tab.
- **Build it yourself:** install the [.NET SDK](https://dotnet.microsoft.com/download) and run

  ```
  dotnet build src/AudioFlipper/AudioFlipper.csproj -c Release
  ```

  The exe is written to `src/AudioFlipper/bin/Release/net48/AudioFlipper.exe`.

Use **Start with Windows** in the right-click menu to launch it at sign-in, and **Exit** to close it.

To publish a new release, set `<Version>` in `src/AudioFlipper/AudioFlipper.csproj` and add release
notes as `.github/release-notes/v<version>.md`. Then on the **Actions** tab, choose **Release →
Run workflow**, pick the branch and enter the version (or push a `v<version>` tag). The workflow
builds the exe, creates the tag and publishes the release with the exe attached.

## Which devices are in the rotation

The rotation is every output (or input) that is enabled and connected (what Windows calls
*active*), in alphabetical order. Devices that are disabled in Windows, or unplugged, are skipped
automatically. To leave out a device you still want enabled (an HDMI monitor's speakers, or a
webcam's built-in microphone, for example), untick it under **Include in click rotation** in the
Output or Input submenu.

By default switching also changes the *communications* device (used by Teams, Discord, etc.) so
calls use the same device. Untick **Also switch communications device** in the Output or Input
submenu to leave it alone.

When the widget isn't showing the output (its section is turned off, or a full-screen app is in
front), a small popup above the taskbar names the new output after each switch. Turn it off with
**Output → Show popup on change**.

## Command line

`AudioFlipper.exe --next` switches to the next output and exits, without a tray icon;
`AudioFlipper.exe --next-input` does the same for microphones. You can use them to get keyboard
shortcuts: create a shortcut to `AudioFlipper.exe --next` on the desktop or in the Start menu, open
its **Properties**, and set a **Shortcut key**. If AudioFlipper is running, its widget and icon
update to show the new device.

## How it works

- Devices, their names and icons, and volume are read with the Windows Core Audio API
  (`IMMDeviceEnumerator`, `IAudioEndpointVolume`).
- Windows has no public API for changing the default output. AudioFlipper uses the undocumented
  `IPolicyConfig` interface that the Sound control panel itself uses; it has been stable since
  Windows 7 and is what other switchers such as EarTrumpet and SoundSwitch rely on.
- Settings are stored in `%APPDATA%\AudioFlipper\settings.txt`.
