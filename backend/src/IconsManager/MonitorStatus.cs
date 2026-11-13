using System.Collections.Generic;

namespace StreamDeckMonitorSwitch
{
    public class MonitorStatus
    {
        public enum PowerStatus
        {
            ON = 0x01,
            SLEEP = 0x04,
            OFF = 0x05,
            UNKNOWN = 0x10
        }

        public uint SelectedInput { get; set; }
        public PowerStatus Power { get; set; }

        // Live readings of continuous (value-range) VCP controls, keyed by the
        // VCP property code name (e.g. "Brightness" -> 72).
        public Dictionary<string, int> ContinuousValues { get; set; } = new Dictionary<string, int>();

        public MonitorStatus() { }
    }
}
