# AudioFlipper

A small Windows 11 tray app for switching audio outputs. Click its icon on the taskbar to change the
default output device to the next one you have enabled (speakers → headphones → monitor → …).

- **Left-click** the icon: switch to the next output device.
- **Hover**: tooltip shows the active device and its volume.
- **Middle-click**: mute / unmute.
- **Right-click**: menu to pick a device directly, choose which devices are in the click rotation,
  and toggle "Start with Windows".

The icon itself shows what kind of device is active (speakers, headphones, headset or monitor) with
a bar along the bottom for the volume level, and follows the light/dark taskbar setting. A small
popup above the taskbar names the new device and its volume after each switch.

## Why a tray icon and not a "widget"?

Windows 11 has no supported way for apps to add custom widgets to the taskbar: the old taskbar
toolbars ("deskbands") were removed, and the Widgets board can't change system settings or sit on
the taskbar. The notification-area (tray) icon is the closest supported equivalent, and it's where
Windows' own volume icon lives.

Windows 11 tucks new tray icons into the `^` overflow menu. To keep AudioFlipper visible on the
taskbar, drag its icon out of the overflow menu, or turn it on under
**Settings → Personalization → Taskbar → Other system tray icons**.

## Getting it

AudioFlipper targets .NET Framework 4.8, which is part of Windows 10 and 11, so there's nothing
else to install.

- **Download:** open the repository's **Actions** tab, select the latest **Build** run, and
  download the `AudioFlipper` artifact. Put `AudioFlipper.exe` anywhere (for example
  `%LOCALAPPDATA%\AudioFlipper`) and run it. Windows SmartScreen may warn about an unsigned app;
  choose **More info → Run anyway**.
- **Build it yourself:** install the [.NET SDK](https://dotnet.microsoft.com/download) and run

  ```
  dotnet build src/AudioFlipper/AudioFlipper.csproj -c Release
  ```

  The exe is written to `src/AudioFlipper/bin/Release/net48/AudioFlipper.exe`.

Use **Start with Windows** in the right-click menu to launch it at sign-in.

## Which devices are in the rotation

The rotation is every output that is enabled and connected (what Windows calls *active*), in
alphabetical order. Devices that are disabled in Windows, or unplugged, are skipped automatically.
To leave out an output you still want enabled (an HDMI monitor's speakers, for example), untick it
under **Include in click rotation** in the right-click menu.

By default switching also changes the *communications* device (used by Teams, Discord, etc.) so
everything plays from the same output. Untick **Also switch communications device** to leave it
alone.

## Command line

`AudioFlipper.exe --next` switches to the next device and exits, without a tray icon. You can use
it to get a keyboard shortcut: create a shortcut to `AudioFlipper.exe --next` on the desktop or in
the Start menu, open its **Properties**, and set a **Shortcut key**. If the tray app is running,
its icon and popup update to show the new device.

## How it works

- Devices and volume are read with the Windows Core Audio API (`IMMDeviceEnumerator`,
  `IAudioEndpointVolume`).
- Windows has no public API for changing the default output. AudioFlipper uses the undocumented
  `IPolicyConfig` interface that the Sound control panel itself uses; it has been stable since
  Windows 7 and is what other switchers such as EarTrumpet and SoundSwitch rely on.
- Settings are stored in `%APPDATA%\AudioFlipper\settings.txt`.
