using System.Collections.Generic;
using StreamDeckMonitorSwitch.Abstraction;

namespace StreamDeckMonitorSwitch.Abstraction
{
    /**
     * Abstraction over monitor state reading (power, input, continuous values).
     * Used by IconManager to decouple state acquisition from rendering.
     */
    public interface IMonitorStateReader
    {
        /**
         * Reads the current power status of a monitor.
         */
        MonitorStatus.PowerStatus ReadPowerStatus(ddcmon.PhysicalMonitor monitor);

        /**
         * Reads the currently selected input source.
         * Returns true on success; selectedInput is populated.
         */
        bool ReadSelectedInput(ddcmon.PhysicalMonitor monitor, out uint selectedInput);

        /**
         * Reads live values for all continuous VCP properties.
         * Returns a dictionary keyed by VCP code name.
         */
        Dictionary<string, int> ReadContinuousValues(ddcmon.PhysicalMonitor monitor, IEnumerable<string> propertyNames);
    }
}
