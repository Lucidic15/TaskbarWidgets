using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace RDPLauncher.Displays
{
    /// <summary>
    /// Reads monitor names (like "DELL U2720Q") with the Windows display configuration API, the
    /// names Settings > System > Display shows.
    /// </summary>
    internal static class DisplayNames
    {
        const uint QdcOnlyActivePaths = 0x2;
        const int ErrorInsufficientBuffer = 122;
        const uint DeviceInfoGetSourceName = 1;
        const uint DeviceInfoGetTargetName = 2;
        const uint OutputTechnologyInternal = 0x80000000;
        const uint OutputTechnologyDisplayPortEmbedded = 11;
        const uint OutputTechnologyUdiEmbedded = 13;

        [StructLayout(LayoutKind.Sequential)]
        struct Luid
        {
            public uint LowPart;
            public int HighPart;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct PathSourceInfo
        {
            public Luid AdapterId;
            public uint Id;
            public uint ModeInfoIndex;
            public uint StatusFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct PathTargetInfo
        {
            public Luid AdapterId;
            public uint Id;
            public uint ModeInfoIndex;
            public uint OutputTechnology;
            public uint Rotation;
            public uint Scaling;
            public uint RefreshRateNumerator;
            public uint RefreshRateDenominator;
            public uint ScanLineOrdering;
            public int TargetAvailable;
            public uint StatusFlags;
        }

        /// <summary>DISPLAYCONFIG_PATH_INFO (72 bytes).</summary>
        [StructLayout(LayoutKind.Sequential)]
        struct PathInfo
        {
            public PathSourceInfo SourceInfo;
            public PathTargetInfo TargetInfo;
            public uint Flags;
        }

        /// <summary>DISPLAYCONFIG_MODE_INFO (64 bytes); only needed as a buffer here.</summary>
        [StructLayout(LayoutKind.Sequential, Size = 64)]
        struct ModeInfo
        {
            public uint InfoType;
            public uint Id;
            public Luid AdapterId;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct DeviceInfoHeader
        {
            public uint Type;
            public uint Size;
            public Luid AdapterId;
            public uint Id;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct SourceDeviceName
        {
            public DeviceInfoHeader Header;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string ViewGdiDeviceName;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct TargetDeviceName
        {
            public DeviceInfoHeader Header;
            public uint Flags;
            public uint OutputTechnology;
            public ushort EdidManufactureId;
            public ushort EdidProductCodeId;
            public uint ConnectorInstance;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string MonitorFriendlyDeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string MonitorDevicePath;
        }

        [DllImport("user32.dll")]
        static extern int GetDisplayConfigBufferSizes(uint flags, out uint pathCount, out uint modeCount);

        [DllImport("user32.dll")]
        static extern int QueryDisplayConfig(uint flags, ref uint pathCount, [Out] PathInfo[] paths,
            ref uint modeCount, [Out] ModeInfo[] modes, IntPtr currentTopologyId);

        [DllImport("user32.dll")]
        static extern int DisplayConfigGetDeviceInfo(ref SourceDeviceName request);

        [DllImport("user32.dll")]
        static extern int DisplayConfigGetDeviceInfo(ref TargetDeviceName request);

        /// <summary>Monitor names by GDI device name (\\.\DISPLAY1); empty if Windows won't say.</summary>
        public static Dictionary<string, string> Get()
        {
            var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (PathInfo path in GetActivePaths())
                {
                    var source = new SourceDeviceName();
                    source.Header.Type = DeviceInfoGetSourceName;
                    source.Header.Size = (uint)Marshal.SizeOf(typeof(SourceDeviceName));
                    source.Header.AdapterId = path.SourceInfo.AdapterId;
                    source.Header.Id = path.SourceInfo.Id;
                    if (DisplayConfigGetDeviceInfo(ref source) != 0 || string.IsNullOrEmpty(source.ViewGdiDeviceName))
                        continue;

                    var target = new TargetDeviceName();
                    target.Header.Type = DeviceInfoGetTargetName;
                    target.Header.Size = (uint)Marshal.SizeOf(typeof(TargetDeviceName));
                    target.Header.AdapterId = path.TargetInfo.AdapterId;
                    target.Header.Id = path.TargetInfo.Id;
                    if (DisplayConfigGetDeviceInfo(ref target) != 0)
                        continue;

                    string name = target.MonitorFriendlyDeviceName?.Trim();
                    if (string.IsNullOrEmpty(name))
                        name = IsBuiltIn(target.OutputTechnology) ? "Built-in display" : null;
                    // A duplicated (mirrored) desktop has several monitors; keep the first name.
                    if (name != null && !names.ContainsKey(source.ViewGdiDeviceName))
                        names[source.ViewGdiDeviceName] = name;
                }
            }
            catch (Exception)
            {
                // Names are only for display; without them displays are just called "Display".
            }
            return names;
        }

        static PathInfo[] GetActivePaths()
        {
            // The configuration can change between asking for the sizes and reading it; try again then.
            for (int attempt = 0; attempt < 3; attempt++)
            {
                if (GetDisplayConfigBufferSizes(QdcOnlyActivePaths, out uint pathCount, out uint modeCount) != 0)
                    break;
                var paths = new PathInfo[pathCount];
                var modes = new ModeInfo[modeCount];
                int result = QueryDisplayConfig(QdcOnlyActivePaths, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
                if (result == 0)
                {
                    Array.Resize(ref paths, (int)pathCount);
                    return paths;
                }
                if (result != ErrorInsufficientBuffer)
                    break;
            }
            return new PathInfo[0];
        }

        static bool IsBuiltIn(uint outputTechnology) =>
            outputTechnology == OutputTechnologyInternal
            || outputTechnology == OutputTechnologyDisplayPortEmbedded
            || outputTechnology == OutputTechnologyUdiEmbedded;
    }
}
