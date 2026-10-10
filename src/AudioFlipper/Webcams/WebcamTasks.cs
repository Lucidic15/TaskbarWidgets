using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Security;
using System.Security.Principal;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AudioFlipper.Webcams
{
    /// <summary>
    /// Lets AudioFlipper turn the chosen webcam on and off without a UAC prompt every time.
    /// </summary>
    /// <remarks>
    /// Setting up asks for administrator rights once and registers two scheduled tasks that run
    /// Windows' pnputil.exe as SYSTEM, one to enable and one to disable that one webcam, which the
    /// signed-in user is allowed to start. Starting a task needs no prompt. The tasks only ever run
    /// pnputil from the Windows folder with that webcam's ID, so they can't be used to run anything else.
    /// </remarks>
    internal static class WebcamTasks
    {
        const string EnableTaskName = "AudioFlipper Webcam On";
        const string DisableTaskName = "AudioFlipper Webcam Off";
        const string InstallArgument = "--install-webcam-tasks";
        const string RemoveArgument = "--remove-webcam-tasks";

        const int TaskCreateOrUpdate = 6;
        const int TaskLogonServiceAccount = 5;
        const int ErrorCancelled = 1223;
        const int ErrorSuccessRebootRequired = 3010;

        /// <summary>
        /// Asks for administrator rights once and sets up the tasks for this webcam. True when done,
        /// false when it failed, null when the UAC prompt was declined.
        /// </summary>
        public static Task<bool?> InstallAsync(string instanceId, IntPtr ownerWindow)
        {
            string userSid = WindowsIdentity.GetCurrent().User?.Value;
            return RunElevatedAsync(InstallArgument + " \"" + instanceId + "\" " + userSid, ownerWindow);
        }

        /// <summary>Asks for administrator rights once and removes the tasks. Results as for <see cref="InstallAsync"/>.</summary>
        public static Task<bool?> RemoveAsync(IntPtr ownerWindow) => RunElevatedAsync(RemoveArgument, ownerWindow);

        /// <summary>
        /// Runs the enable or disable task and waits for the webcam to change. Returns null when the
        /// tasks aren't set up (or were deleted), so the caller can fall back to the UAC prompt.
        /// </summary>
        public static Task<WebcamChangeResult?> TrySetEnabledAsync(string instanceId, bool enable)
        {
            return Task.Run<WebcamChangeResult?>(() =>
            {
                dynamic task;
                try
                {
                    dynamic service = ConnectToTaskScheduler();
                    task = service.GetFolder("\\").GetTask(enable ? EnableTaskName : DisableTaskName);
                    task.Run(null);
                }
                catch (Exception)
                {
                    return null;
                }

                // The task runs in the background; watch for the webcam to change.
                for (int waited = 0; waited < 10000; waited += 200)
                {
                    if (WebcamDevices.IsEnabled(instanceId) == enable)
                        return WebcamChangeResult.Done;
                    Thread.Sleep(200);
                }
                try
                {
                    if ((int)task.LastTaskResult == ErrorSuccessRebootRequired)
                        return WebcamChangeResult.RestartNeeded;
                }
                catch (Exception)
                {
                }
                return WebcamChangeResult.Failed;
            });
        }

        /// <summary>
        /// Handles the elevated "--install-webcam-tasks" and "--remove-webcam-tasks" runs of
        /// AudioFlipper.exe. Returns the process exit code, or null when the arguments are neither.
        /// </summary>
        public static int? HandleElevatedCommand(string[] args)
        {
            try
            {
                if (args.Length == 3 && args[0] == InstallArgument)
                    return Install(args[1], args[2]) ? 0 : 1;
                if (args.Length == 1 && args[0] == RemoveArgument)
                {
                    Remove();
                    return 0;
                }
            }
            catch (Exception)
            {
                return 1;
            }
            return null;
        }

        static bool Install(string instanceId, string userSid)
        {
            // Check everything again here, since this runs with administrator rights: only a connected
            // webcam's ID, and a well-formed SID that's safe to put in the security descriptor.
            if (instanceId.Contains("\"") || !WebcamDevices.GetAll().Any(w => w.InstanceId == instanceId))
                return false;
            if (!Regex.IsMatch(userSid ?? "", @"^S-1-[0-9]+(-[0-9]+)+$"))
                return false;

            // SYSTEM and administrators have full control; the user can read and start the tasks.
            string sddl = "D:(A;;FA;;;SY)(A;;FA;;;BA)(A;;FRFX;;;" + userSid + ")";
            dynamic root = ConnectToTaskScheduler().GetFolder("\\");
            root.RegisterTask(EnableTaskName, TaskXml("Turns on", "/enable-device \"" + instanceId + "\""),
                TaskCreateOrUpdate, "SYSTEM", null, TaskLogonServiceAccount, sddl);
            root.RegisterTask(DisableTaskName, TaskXml("Turns off", "/disable-device \"" + instanceId + "\""),
                TaskCreateOrUpdate, "SYSTEM", null, TaskLogonServiceAccount, sddl);
            return true;
        }

        static void Remove()
        {
            dynamic root = ConnectToTaskScheduler().GetFolder("\\");
            foreach (string name in new[] { EnableTaskName, DisableTaskName })
            {
                try
                {
                    root.DeleteTask(name, 0);
                }
                catch (Exception)
                {
                    // Already gone.
                }
            }
        }

        static string TaskXml(string whatItDoes, string pnputilArguments) =>
            "<?xml version=\"1.0\" encoding=\"UTF-16\"?>" +
            "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">" +
            "<RegistrationInfo><Author>AudioFlipper</Author><Description>" +
            SecurityElement.Escape(whatItDoes + " the webcam chosen in AudioFlipper. Started by AudioFlipper when its webcam button is clicked.") +
            "</Description></RegistrationInfo>" +
            "<Principals><Principal id=\"Author\"><UserId>S-1-5-18</UserId><RunLevel>HighestAvailable</RunLevel></Principal></Principals>" +
            "<Settings>" +
            "<MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>" +
            "<DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>" +
            "<StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>" +
            "<AllowStartOnDemand>true</AllowStartOnDemand>" +
            "<Enabled>true</Enabled>" +
            "<ExecutionTimeLimit>PT1M</ExecutionTimeLimit>" +
            "</Settings>" +
            "<Actions Context=\"Author\"><Exec>" +
            "<Command>%SystemRoot%\\System32\\pnputil.exe</Command>" +
            "<Arguments>" + SecurityElement.Escape(pnputilArguments) + "</Arguments>" +
            "</Exec></Actions>" +
            "</Task>";

        static dynamic ConnectToTaskScheduler()
        {
            dynamic service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service", true));
            service.Connect();
            return service;
        }

        static Task<bool?> RunElevatedAsync(string arguments, IntPtr ownerWindow)
        {
            return Task.Run<bool?>(() =>
            {
                var start = new ProcessStartInfo(Application.ExecutablePath, arguments)
                {
                    UseShellExecute = true,
                    Verb = "runas",
                    ErrorDialogParentHandle = ownerWindow,
                };
                try
                {
                    using (Process process = Process.Start(start))
                    {
                        process.WaitForExit();
                        return process.ExitCode == 0;
                    }
                }
                catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
                {
                    return null; // "No" on the UAC prompt.
                }
            });
        }
    }
}
