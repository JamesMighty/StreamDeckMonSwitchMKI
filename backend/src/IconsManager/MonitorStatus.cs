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

        public MonitorStatus() { }
    }
}
