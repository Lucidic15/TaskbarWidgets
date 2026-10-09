using System;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using AudioFlipper.Audio;

namespace AudioFlipper
{
    internal static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            if (args.Any(arg => string.Equals(arg, "--next", StringComparison.OrdinalIgnoreCase)))
                return SwitchOnce();

            using (new Mutex(true, @"Local\AudioFlipper.SingleInstance", out bool isFirstInstance))
            {
                if (!isFirstInstance)
                    return 0; // Already running in the tray.

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                using (var app = new TrayApp())
                    Application.Run(app);
            }
            return 0;
        }

        /// <summary>
        /// "AudioFlipper.exe --next" switches to the next output and exits without a tray icon.
        /// Useful as a shortcut with a keyboard hotkey, or pinned to the taskbar.
        /// </summary>
        static int SwitchOnce()
        {
            try
            {
                using (var audio = new AudioDeviceManager())
                    return OutputCycler.SwitchToNext(audio, Settings.Load()) != null ? 0 : 1;
            }
            catch (Exception)
            {
                return 1;
            }
        }
    }
}
