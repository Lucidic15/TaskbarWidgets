using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace AudioFlipper.Webcams
{
    internal sealed class Webcam
    {
        public Webcam(string instanceId, string name, bool enabled)
        {
            InstanceId = instanceId;
            Name = name;
            Enabled = enabled;
        }

        /// <summary>Device instance ID, e.g. "USB\VID_046D&amp;PID_085E&amp;MI_00\7&amp;1A2B3C&amp;0&amp;0000".</summary>
        public string InstanceId { get; }

        public string Name { get; }

        /// <summary>False when the device is disabled (as with Disable device in Device Manager).</summary>
        public bool Enabled { get; }
    }

    /// <summary>What the taskbar widget shows for the webcam being controlled.</summary>
    internal sealed class WebcamStatus
    {
        public WebcamStatus(bool enabled, bool busy)
        {
            Enabled = enabled;
            Busy = busy;
        }

        public bool Enabled { get; }

        /// <summary>Being turned on or off (waiting on the UAC prompt or Windows).</summary>
        public bool Busy { get; }

        public static bool AreEqual(WebcamStatus a, WebcamStatus b)
        {
            if (a == null || b == null)
                return a == b;
            return a.Enabled == b.Enabled && a.Busy == b.Busy;
        }
    }

    internal enum WebcamChangeResult
    {
        Done,
        RestartNeeded,
        Cancelled,
        Failed,
    }

    /// <summary>
    /// Finds webcams and turns them on or off the way Device Manager's Enable/Disable device does.
    /// Reading is instant and needs no special rights; turning a device on or off needs administrator
    /// rights, so it runs Windows' PnP Utility (pnputil.exe) elevated, which shows a UAC prompt unless
    /// AudioFlipper itself is running as administrator.
    /// </summary>
    internal static class WebcamDevices
    {
        // Device setup classes webcams install under: "Cameras", and "Imaging devices" for older drivers.
        static readonly Guid[] CameraClasses =
        {
            new Guid("CA3E7AB9-B4C3-4AE6-8251-579EF933890F"),
            new Guid("6BDD1FC6-810F-11D0-BEC7-08002BE2092F"),
        };

        const int ErrorCancelled = 1223;
        const int ErrorSuccessRebootRequired = 3010;

        /// <summary>Connected webcams, including disabled ones, sorted by name.</summary>
        public static List<Webcam> GetAll()
        {
            var webcams = new List<Webcam>();
            foreach (Guid classGuid in CameraClasses)
            {
                Guid guid = classGuid;
                IntPtr deviceSet = SetupDiGetClassDevs(ref guid, null, IntPtr.Zero, DigcfPresent);
                if (deviceSet == InvalidHandleValue)
                    continue;
                try
                {
                    var device = new SpDevInfoData { Size = (uint)Marshal.SizeOf(typeof(SpDevInfoData)) };
                    for (uint index = 0; SetupDiEnumDeviceInfo(deviceSet, index, ref device); index++)
                    {
                        string instanceId = GetInstanceId(deviceSet, ref device);
                        if (instanceId == null || webcams.Any(w => w.InstanceId == instanceId))
                            continue;
                        string name = GetStringProperty(deviceSet, ref device, SpdrpFriendlyName)
                            ?? GetStringProperty(deviceSet, ref device, SpdrpDeviceDesc)
                            ?? instanceId;
                        webcams.Add(new Webcam(instanceId, name, IsEnabled(device.DevInst)));
                    }
                }
                finally
                {
                    SetupDiDestroyDeviceInfoList(deviceSet);
                }
            }

            webcams.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
            return webcams;
        }

        /// <summary>
        /// Whether the webcam is enabled, or null when it isn't connected. Cheap enough to poll.
        /// </summary>
        public static bool? IsEnabled(string instanceId)
        {
            if (string.IsNullOrEmpty(instanceId) || CM_Locate_DevNode(out uint devInst, instanceId, CmLocateDevnodeNormal) != CrSuccess)
                return null;
            return IsEnabled(devInst);
        }

        /// <summary>
        /// Turns the webcam on or off. Shows a UAC prompt, owned by <paramref name="ownerWindow"/>,
        /// unless AudioFlipper is running as administrator.
        /// </summary>
        public static Task<WebcamChangeResult> SetEnabledAsync(string instanceId, bool enable, IntPtr ownerWindow)
        {
            // Only ever hand pnputil the ID of a webcam that's actually connected, never anything
            // else read from the settings file, since it runs with administrator rights.
            if (string.IsNullOrEmpty(instanceId) || instanceId.Contains("\"") || !GetAll().Any(w => w.InstanceId == instanceId))
                return Task.FromResult(WebcamChangeResult.Failed);

            return Task.Run(() =>
            {
                var start = new ProcessStartInfo(
                    Path.Combine(Environment.SystemDirectory, "pnputil.exe"),
                    (enable ? "/enable-device " : "/disable-device ") + "\"" + instanceId + "\"")
                {
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden,
                    ErrorDialogParentHandle = ownerWindow,
                };

                int exitCode;
                try
                {
                    using (Process process = Process.Start(start))
                    {
                        process.WaitForExit();
                        exitCode = process.ExitCode;
                    }
                }
                catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
                {
                    return WebcamChangeResult.Cancelled; // "No" on the UAC prompt.
                }

                // Trust the device's actual state over pnputil's exit code.
                bool? enabledNow = IsEnabled(instanceId);
                if (enabledNow == enable)
                    return exitCode == ErrorSuccessRebootRequired ? WebcamChangeResult.RestartNeeded : WebcamChangeResult.Done;
                return exitCode == ErrorSuccessRebootRequired ? WebcamChangeResult.RestartNeeded : WebcamChangeResult.Failed;
            });
        }

        static bool IsEnabled(uint devInst)
        {
            if (CM_Get_DevNode_Status(out uint status, out uint problem, devInst, 0) != CrSuccess)
                return true;
            return !((status & DnHasProblem) != 0 && problem == CmProbDisabled);
        }

        static string GetInstanceId(IntPtr deviceSet, ref SpDevInfoData device)
        {
            var id = new StringBuilder(512);
            return SetupDiGetDeviceInstanceId(deviceSet, ref device, id, id.Capacity, out _) ? id.ToString() : null;
        }

        static string GetStringProperty(IntPtr deviceSet, ref SpDevInfoData device, uint property)
        {
            var buffer = new byte[1024];
            if (!SetupDiGetDeviceRegistryProperty(deviceSet, ref device, property, out _, buffer, (uint)buffer.Length, out uint size) || size < 2)
                return null;
            string value = Encoding.Unicode.GetString(buffer, 0, (int)size).TrimEnd('\0');
            return value.Length > 0 ? value : null;
        }

        // SetupAPI and Configuration Manager declarations (setupapi.h, cfgmgr32.h).

        static readonly IntPtr InvalidHandleValue = new IntPtr(-1);
        const uint DigcfPresent = 0x2;
        const uint SpdrpDeviceDesc = 0x0;
        const uint SpdrpFriendlyName = 0xC;
        const uint CmLocateDevnodeNormal = 0x0;
        const int CrSuccess = 0;
        const uint DnHasProblem = 0x400;
        const uint CmProbDisabled = 22;

        [StructLayout(LayoutKind.Sequential)]
        struct SpDevInfoData
        {
            public uint Size;
            public Guid ClassGuid;
            public uint DevInst;
            public IntPtr Reserved;
        }

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, string enumerator, IntPtr parent, uint flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool SetupDiEnumDeviceInfo(IntPtr deviceSet, uint index, ref SpDevInfoData device);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool SetupDiGetDeviceInstanceId(IntPtr deviceSet, ref SpDevInfoData device, StringBuilder instanceId, int size, out int requiredSize);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool SetupDiGetDeviceRegistryProperty(IntPtr deviceSet, ref SpDevInfoData device, uint property,
            out uint registryType, byte[] buffer, uint bufferSize, out uint requiredSize);

        [DllImport("setupapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceSet);

        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
        static extern int CM_Locate_DevNode(out uint devInst, string deviceId, uint flags);

        [DllImport("cfgmgr32.dll")]
        static extern int CM_Get_DevNode_Status(out uint status, out uint problem, uint devInst, uint flags);
    }
}
