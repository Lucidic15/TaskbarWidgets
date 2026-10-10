using System;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using AudioFlipper.Audio;
using AudioFlipper.Webcams;

namespace AudioFlipper
{
    internal static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            if (args.Any(arg => string.Equals(arg, "--next", StringComparison.OrdinalIgnoreCase)))
                return SwitchOnce(EDataFlow.Render);
            if (args.Any(arg => string.Equals(arg, "--next-input", StringComparison.OrdinalIgnoreCase)))
                return SwitchOnce(EDataFlow.Capture);

            // One-time elevated runs that set up or remove the webcam tasks.
            int? webcamTasksResult = WebcamTasks.HandleElevatedCommand(args);
            if (webcamTasksResult != null)
                return webcamTasksResult.Value;

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
        /// "AudioFlipper.exe --next" (or "--next-input" for microphones) switches to the next device
        /// and exits without a tray icon. Useful as a shortcut with a keyboard hotkey.
        /// </summary>
        static int SwitchOnce(EDataFlow flow)
        {
            try
            {
                using (var audio = new AudioDeviceManager())
                    return DeviceCycler.SwitchToNext(audio, flow, Settings.Load()) != null ? 0 : 1;
            }
            catch (Exception)
            {
                return 1;
            }
        }
    }
}
