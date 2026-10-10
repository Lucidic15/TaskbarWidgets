using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace AudioFlipper.Audio
{
    internal enum DeviceKind
    {
        Speakers,
        Headphones,
        Headset,
        Display,
        Microphone,
    }

    internal sealed class AudioDevice
    {
        public AudioDevice(string id, string name, string description, string fullName, DeviceKind kind, string iconPath)
        {
            Id = id;
            Name = name;
            Description = description;
            FullName = fullName;
            Kind = kind;
            IconPath = iconPath;
        }

        public string Id { get; }

        /// <summary>The device's own name, which can be changed in the Sound control panel, e.g. "Headset".</summary>
        public string Name { get; }

        /// <summary>The audio hardware, e.g. "4- USB Advanced Audio Device".</summary>
        public string Description { get; }

        /// <summary>Name plus the audio hardware, e.g. "Headset (4- USB Advanced Audio Device)".</summary>
        public string FullName { get; }

        public DeviceKind Kind { get; }

        /// <summary>The icon chosen in the Sound control panel, e.g. "%SystemRoot%\System32\mmres.dll,-3011".</summary>
        public string IconPath { get; }
    }

    internal sealed class AudioStatus
    {
        public AudioStatus(AudioDevice device, float volumeLevel, bool muted)
        {
            Device = device;
            VolumeLevel = volumeLevel;
            Muted = muted;
        }

        public AudioDevice Device { get; }

        /// <summary>Volume from 0 to 1.</summary>
        public float VolumeLevel { get; }

        public int VolumePercent => (int)Math.Round(VolumeLevel * 100);

        public bool Muted { get; }

        public static bool AreEqual(AudioStatus a, AudioStatus b)
        {
            if (a == null || b == null)
                return a == b;
            return a.Device.Id == b.Device.Id
                && a.Device.Name == b.Device.Name
                && a.Device.Description == b.Device.Description
                && a.Device.FullName == b.Device.FullName
                && a.Device.IconPath == b.Device.IconPath
                && a.VolumeLevel == b.VolumeLevel
                && a.Muted == b.Muted;
        }
    }

    /// <summary>
    /// Lists output (Render) and input (Capture) devices, reads the default device's volume and
    /// changes the default device, using the Windows Core Audio API.
    /// </summary>
    internal sealed class AudioDeviceManager : IDisposable
    {
        const uint ClsCtxAll = 0x17;
        const uint StgmRead = 0;
        const ushort VtUi4 = 19;
        const ushort VtLpwstr = 31;
        const int ENotFound = unchecked((int)0x80070490);

        static readonly PropertyKey FriendlyNameKey = new PropertyKey(new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 14);
        // PKEY_Device_DeviceDesc: for an endpoint, its own name ("Headset").
        static readonly PropertyKey EndpointNameKey = new PropertyKey(new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 2);
        // PKEY_DeviceInterface_FriendlyName: the audio hardware ("4- USB Advanced Audio Device").
        static readonly PropertyKey InterfaceFriendlyNameKey = new PropertyKey(new Guid("026E516E-B814-414B-83CD-856D6FEF4822"), 2);
        static readonly PropertyKey IconPathKey = new PropertyKey(new Guid("259ABFFC-50A7-47CE-AF08-68C9A7D73366"), 12);
        static readonly PropertyKey FormFactorKey = new PropertyKey(new Guid("1DA5D803-D492-4EDD-8C23-E0C0FFEE7F0E"), 0);
        static readonly Guid AudioEndpointVolumeId = new Guid("5CDF2C82-841E-4546-9722-0CF74078229A");

        readonly IMMDeviceEnumerator enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();

        // The default device's volume control, per direction, is kept between polls and only
        // reactivated when the default device changes.
        readonly CachedVolume[] volumes = { new CachedVolume(), new CachedVolume() };

        sealed class CachedVolume
        {
            public string DeviceId;
            public IAudioEndpointVolume Control;
        }

        /// <summary>Enabled, connected devices in that direction, sorted by name.</summary>
        public List<AudioDevice> GetActiveDevices(EDataFlow flow)
        {
            var devices = new List<AudioDevice>();
            enumerator.EnumAudioEndpoints(flow, DeviceState.Active, out IMMDeviceCollection collection);
            try
            {
                collection.GetCount(out uint count);
                for (uint i = 0; i < count; i++)
                {
                    collection.Item(i, out IMMDevice device);
                    try
                    {
                        devices.Add(ReadDevice(device));
                    }
                    finally
                    {
                        Release(device);
                    }
                }
            }
            finally
            {
                Release(collection);
            }

            devices.Sort((a, b) =>
            {
                int byName = string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
                return byName != 0 ? byName : string.Compare(a.FullName, b.FullName, StringComparison.CurrentCultureIgnoreCase);
            });
            return devices;
        }

        /// <summary>ID of the default device in that direction, or null if there is none.</summary>
        public string GetDefaultDeviceId(EDataFlow flow)
        {
            IMMDevice device = GetDefaultDevice(flow);
            if (device == null)
                return null;
            try
            {
                device.GetId(out string id);
                return id;
            }
            finally
            {
                Release(device);
            }
        }

        /// <summary>The default device in that direction with its volume, or null if there is none.</summary>
        public AudioStatus GetDefaultStatus(EDataFlow flow)
        {
            CachedVolume cache = volumes[(int)flow];
            IMMDevice device = GetDefaultDevice(flow);
            if (device == null)
            {
                ReleaseVolume(cache);
                return null;
            }

            try
            {
                // Name and icon are re-read every time so renames in Windows show up straight away.
                AudioDevice info = ReadDevice(device);
                if (cache.Control == null || cache.DeviceId != info.Id)
                {
                    ReleaseVolume(cache);
                    Guid iid = AudioEndpointVolumeId;
                    device.Activate(ref iid, ClsCtxAll, IntPtr.Zero, out object endpointVolume);
                    cache.Control = (IAudioEndpointVolume)endpointVolume;
                    cache.DeviceId = info.Id;
                }

                cache.Control.GetMasterVolumeLevelScalar(out float level);
                cache.Control.GetMute(out bool muted);
                return new AudioStatus(info, level, muted);
            }
            catch (COMException)
            {
                // Usually the device was unplugged between calls; start over on the next poll.
                ReleaseVolume(cache);
                throw;
            }
            finally
            {
                Release(device);
            }
        }

        /// <summary>Makes the device (output or input) the default for its direction.</summary>
        public void SetDefaultDevice(string id, bool includeCommunications)
        {
            var policyConfig = (IPolicyConfig)new PolicyConfigClientComObject();
            try
            {
                Marshal.ThrowExceptionForHR(policyConfig.SetDefaultEndpoint(id, ERole.Console));
                Marshal.ThrowExceptionForHR(policyConfig.SetDefaultEndpoint(id, ERole.Multimedia));
                if (includeCommunications)
                    Marshal.ThrowExceptionForHR(policyConfig.SetDefaultEndpoint(id, ERole.Communications));
            }
            finally
            {
                Release(policyConfig);
            }
        }

        public void ToggleDefaultMute(EDataFlow flow)
        {
            if (GetDefaultStatus(flow) == null)
                return;
            IAudioEndpointVolume volume = volumes[(int)flow].Control;
            volume.GetMute(out bool muted);
            Guid eventContext = Guid.Empty;
            volume.SetMute(!muted, ref eventContext);
        }

        /// <summary>Changes the default device's volume by whole percentage points, unmuting when turning it up.</summary>
        public void ChangeDefaultVolume(EDataFlow flow, int percentagePoints)
        {
            AudioStatus status = GetDefaultStatus(flow);
            if (status != null)
                SetVolume(volumes[(int)flow].Control, (status.VolumePercent + percentagePoints) / 100f, unmute: percentagePoints > 0);
        }

        /// <summary>
        /// Sets the default device's volume (0 to 1), unmuting it like the Windows volume slider does.
        /// Called for every slider movement, so it reuses the volume control from the last poll
        /// instead of looking the device up again.
        /// </summary>
        public void SetDefaultVolume(EDataFlow flow, float level)
        {
            CachedVolume cache = volumes[(int)flow];
            if (cache.Control == null && GetDefaultStatus(flow) == null)
                return;
            try
            {
                SetVolume(cache.Control, level, unmute: level > 0);
            }
            catch (COMException)
            {
                ReleaseVolume(cache); // The device went away; the next poll picks up the new default.
                throw;
            }
        }

        static void SetVolume(IAudioEndpointVolume volume, float level, bool unmute)
        {
            Guid eventContext = Guid.Empty;
            volume.SetMasterVolumeLevelScalar(Math.Max(0f, Math.Min(1f, level)), ref eventContext);
            if (unmute)
            {
                volume.GetMute(out bool muted);
                if (muted)
                    volume.SetMute(false, ref eventContext);
            }
        }

        public void Dispose()
        {
            foreach (CachedVolume cache in volumes)
                ReleaseVolume(cache);
            Release(enumerator);
        }

        IMMDevice GetDefaultDevice(EDataFlow flow)
        {
            int hr = enumerator.GetDefaultAudioEndpoint(flow, ERole.Console, out IMMDevice device);
            if (hr == ENotFound)
                return null;
            Marshal.ThrowExceptionForHR(hr);
            return device;
        }

        static void ReleaseVolume(CachedVolume cache)
        {
            Release(cache.Control);
            cache.Control = null;
            cache.DeviceId = null;
        }

        static AudioDevice ReadDevice(IMMDevice device)
        {
            device.GetId(out string id);
            device.OpenPropertyStore(StgmRead, out IPropertyStore store);
            try
            {
                string fullName = ReadString(store, FriendlyNameKey);
                string name = ReadString(store, EndpointNameKey);
                if (string.IsNullOrWhiteSpace(name))
                    name = fullName ?? "Unknown device";
                return new AudioDevice(
                    id,
                    name,
                    ReadString(store, InterfaceFriendlyNameKey),
                    fullName ?? name,
                    KindFromFormFactor(ReadUInt(store, FormFactorKey)),
                    ReadString(store, IconPathKey));
            }
            finally
            {
                Release(store);
            }
        }

        // Values of the EndpointFormFactor enum (mmdeviceapi.h).
        static DeviceKind KindFromFormFactor(uint? formFactor)
        {
            switch (formFactor)
            {
                case 3: // Headphones
                    return DeviceKind.Headphones;
                case 4: // Microphone
                    return DeviceKind.Microphone;
                case 5: // Headset
                case 6: // Handset
                    return DeviceKind.Headset;
                case 9: // DigitalAudioDisplayDevice (HDMI / DisplayPort monitor)
                    return DeviceKind.Display;
                default:
                    return DeviceKind.Speakers;
            }
        }

        static string ReadString(IPropertyStore store, PropertyKey key)
        {
            store.GetValue(ref key, out PropVariant value);
            try
            {
                return value.VarType == VtLpwstr ? Marshal.PtrToStringUni(value.PointerValue) : null;
            }
            finally
            {
                PropVariant.PropVariantClear(ref value);
            }
        }

        static uint? ReadUInt(IPropertyStore store, PropertyKey key)
        {
            store.GetValue(ref key, out PropVariant value);
            try
            {
                return value.VarType == VtUi4 ? value.UIntValue : (uint?)null;
            }
            finally
            {
                PropVariant.PropVariantClear(ref value);
            }
        }

        static void Release(object comObject)
        {
            if (comObject != null && Marshal.IsComObject(comObject))
                Marshal.ReleaseComObject(comObject);
        }
    }
}
