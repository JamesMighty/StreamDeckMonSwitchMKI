using System;
using System.Collections.Generic;
using StreamDeckMonitorSwitch.ddcmon;

namespace StreamDeckMonitorSwitch.Abstraction
{
    /**
     * Abstraction over monitor enumeration and lifecycle.
     * Decouples consumers (IconManager, MonitorPluginAction) from the
     * static MonitorConfigurator implementation.
     */
    public interface IMonitorService
    {
        /** Currently detected physical monitors. */
        IReadOnlyList<PhysicalMonitor> Monitors { get; }

        /** Re-enumerates all physical monitors. */
        void UpdateMonitors();

        /** Fired after a successful monitor re-enumeration. */
        event Action OnUpdateMonitors;
    }
}
