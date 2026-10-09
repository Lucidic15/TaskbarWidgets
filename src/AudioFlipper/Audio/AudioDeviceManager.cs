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
    }

    internal sealed class AudioDevice
    {
        public AudioDevice(string id, string name, DeviceKind kind)
        {
            Id = id;
            Name = name;
            Kind = kind;
        }

        public string Id { get; }

        /// <summary>Name as shown in Windows Sound settings, e.g. "Speakers (Realtek(R) Audio)".</summary>
        public string Name { get; }

        public DeviceKind Kind { get; }
    }

    internal sealed class OutputStatus
    {
        public OutputStatus(AudioDevice device, int volumePercent, bool muted)
        {
            Device = device;
            VolumePercent = volumePercent;
            Muted = muted;
        }

        public AudioDevice Device { get; }

        public int VolumePercent { get; }

        public bool Muted { get; }

        public static bool AreEqual(OutputStatus a, OutputStatus b)
        {
            if (a == null || b == null)
                return a == b;
            return a.Device.Id == b.Device.Id
                && a.Device.Name == b.Device.Name
                && a.VolumePercent == b.VolumePercent
                && a.Muted == b.Muted;
        }
    }

    /// <summary>
    /// Lists output devices, reads the default output's volume and changes the default output,
    /// using the Windows Core Audio API.
    /// </summary>
    internal sealed class AudioDeviceManager : IDisposable
    {
        const uint ClsCtxAll = 0x17;
        const uint StgmRead = 0;
        const ushort VtUi4 = 19;
        const ushort VtLpwstr = 31;
        const int ENotFound = unchecked((int)0x80070490);

        static readonly PropertyKey FriendlyNameKey = new PropertyKey(new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 14);
        static readonly PropertyKey FormFactorKey = new PropertyKey(new Guid("1DA5D803-D492-4EDD-8C23-E0C0FFEE7F0E"), 0);
        static readonly Guid AudioEndpointVolumeId = new Guid("5CDF2C82-841E-4546-9722-0CF74078229A");

        readonly IMMDeviceEnumerator enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();

        // The default output and its volume control are kept between polls and
        // reloaded only when the default output changes.
        AudioDevice cachedDevice;
        IAudioEndpointVolume cachedVolume;

        /// <summary>Enabled, connected output devices, sorted by name.</summary>
        public List<AudioDevice> GetActiveOutputDevices()
        {
            var devices = new List<AudioDevice>();
            enumerator.EnumAudioEndpoints(EDataFlow.Render, DeviceState.Active, out IMMDeviceCollection collection);
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

            devices.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
            return devices;
        }

        /// <summary>ID of the default output device, or null if there is none.</summary>
        public string GetDefaultOutputDeviceId()
        {
            int hr = enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Console, out IMMDevice device);
            if (hr == ENotFound)
                return null;
            Marshal.ThrowExceptionForHR(hr);
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

        /// <summary>The default output device with its volume, or null if there is none.</summary>
        public OutputStatus GetDefaultOutputStatus()
        {
            string id = GetDefaultOutputDeviceId();
            if (id == null)
            {
                ClearCache();
                return null;
            }

            if (cachedDevice == null || cachedDevice.Id != id)
            {
                ClearCache();
                LoadCache(id);
            }

            try
            {
                cachedVolume.GetMasterVolumeLevelScalar(out float level);
                cachedVolume.GetMute(out bool muted);
                return new OutputStatus(cachedDevice, (int)Math.Round(level * 100), muted);
            }
            catch (COMException)
            {
                // Usually the device was unplugged between calls; reload on the next poll.
                ClearCache();
                throw;
            }
        }

        public void SetDefaultOutputDevice(string id, bool includeCommunications)
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

        public void ToggleDefaultOutputMute()
        {
            if (GetDefaultOutputStatus() == null)
                return;
            cachedVolume.GetMute(out bool muted);
            Guid eventContext = Guid.Empty;
            cachedVolume.SetMute(!muted, ref eventContext);
        }

        public void Dispose()
        {
            ClearCache();
            Release(enumerator);
        }

        void LoadCache(string id)
        {
            enumerator.GetDevice(id, out IMMDevice device);
            try
            {
                AudioDevice info = ReadDevice(device);
                Guid iid = AudioEndpointVolumeId;
                device.Activate(ref iid, ClsCtxAll, IntPtr.Zero, out object volume);
                cachedVolume = (IAudioEndpointVolume)volume;
                cachedDevice = info;
            }
            finally
            {
                Release(device);
            }
        }

        void ClearCache()
        {
            Release(cachedVolume);
            cachedVolume = null;
            cachedDevice = null;
        }

        static AudioDevice ReadDevice(IMMDevice device)
        {
            device.GetId(out string id);
            device.OpenPropertyStore(StgmRead, out IPropertyStore store);
            try
            {
                string name = ReadString(store, FriendlyNameKey) ?? "Unknown device";
                return new AudioDevice(id, name, KindFromFormFactor(ReadUInt(store, FormFactorKey)));
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
