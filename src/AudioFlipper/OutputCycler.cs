using System.Collections.Generic;
using System.Linq;
using AudioFlipper.Audio;

namespace AudioFlipper
{
    internal static class OutputCycler
    {
        /// <summary>
        /// Makes the device after the current default (in name order, skipping devices
        /// excluded from the rotation) the default output. Returns that device, or null
        /// when there are no active outputs.
        /// </summary>
        public static AudioDevice SwitchToNext(AudioDeviceManager audio, Settings settings)
        {
            List<AudioDevice> devices = audio.GetActiveOutputDevices();
            List<AudioDevice> rotation = devices.Where(d => !settings.IsExcluded(d.Id)).ToList();
            if (rotation.Count == 0)
                rotation = devices; // Everything is excluded: rotate through all of them instead.
            if (rotation.Count == 0)
                return null;

            string currentId = audio.GetDefaultOutputDeviceId();
            int index = rotation.FindIndex(d => d.Id == currentId); // -1 starts from the first device.
            AudioDevice next = rotation[(index + 1) % rotation.Count];
            if (next.Id != currentId)
                audio.SetDefaultOutputDevice(next.Id, settings.SwitchCommunications);
            return next;
        }
    }
}
