using System;
using System.Collections.Generic;
using StreamDeckMonitorSwitch.Abstraction;
using StreamDeckMonitorSwitch.ddcmon;

namespace StreamDeckMonitorSwitch.Abstractions
{
    /**
     * Production implementation of IMonitorService wrapping the static MonitorConfigurator.
     */
    public class MonitorService : IMonitorService
    {
        public IReadOnlyList<PhysicalMonitor> Monitors => MonitorConfigurator.Monitors;

        public event Action OnUpdateMonitors
        {
            add => MonitorConfigurator.OnUpdateMonitors += value;
            remove => MonitorConfigurator.OnUpdateMonitors -= value;
        }

        public void UpdateMonitors()
        {
            MonitorConfigurator.UpdateMonitors();
        }
    }
}
