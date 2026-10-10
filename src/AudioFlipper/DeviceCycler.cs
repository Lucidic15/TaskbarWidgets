using System.Collections.Generic;
using System.Linq;
using AudioFlipper.Audio;

namespace AudioFlipper
{
    internal static class DeviceCycler
    {
        /// <summary>
        /// Makes the device after the current default (in name order, skipping devices excluded
        /// from the rotation) the default output or input. Returns that device, or null when there
        /// are no active devices in that direction.
        /// </summary>
        public static AudioDevice SwitchToNext(AudioDeviceManager audio, EDataFlow flow, Settings settings)
        {
            List<AudioDevice> devices = audio.GetActiveDevices(flow);
            List<AudioDevice> rotation = devices.Where(d => !settings.IsExcluded(d.Id)).ToList();
            if (rotation.Count == 0)
                rotation = devices; // Everything is excluded: rotate through all of them instead.
            if (rotation.Count == 0)
                return null;

            string currentId = audio.GetDefaultDeviceId(flow);
            int index = rotation.FindIndex(d => d.Id == currentId); // -1 starts from the first device.
            AudioDevice next = rotation[(index + 1) % rotation.Count];
            if (next.Id != currentId)
                audio.SetDefaultDevice(next.Id, settings.SwitchesCommunications(flow));
            return next;
        }
    }
}
