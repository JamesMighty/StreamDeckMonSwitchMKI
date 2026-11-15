using System;
using System.Collections.Generic;
using StreamDeckMonitorSwitch.Abstraction;
using StreamDeckMonitorSwitch.ddcmon;

namespace StreamDeckMonitorSwitch.Abstractions
{
    /**
     * Production implementation of IMonitorStateReader using DDC/CI reads.
     */
    public class MonitorStateReader : IMonitorStateReader
    {
        private readonly IDdcCiDriver _driver;

        public MonitorStateReader(IDdcCiDriver driver)
        {
            _driver = driver ?? throw new ArgumentNullException(nameof(driver));
        }

        public MonitorStatus.PowerStatus ReadPowerStatus(PhysicalMonitor monitor)
        {
            byte powerMode = 0;
            byte max = 0;

            if (_driver.GetVcpFeature(monitor.Struct.hPhysicalMonitor, VCP_PROPS.POWER_MODE.code, ref powerMode, ref max))
            {
                return (MonitorStatus.PowerStatus)powerMode;
            }

            bool isOn = false;
            if (_driver.GetDevicePowerState(monitor.device, ref isOn))
            {
                return isOn ? MonitorStatus.PowerStatus.ON : MonitorStatus.PowerStatus.OFF;
            }

            return MonitorStatus.PowerStatus.UNKNOWN;
        }

        public bool ReadSelectedInput(PhysicalMonitor monitor, out uint selectedInput)
        {
            byte currentValue = 0;
            byte maxValue = 0;
            selectedInput = 0;

            if (_driver.GetVcpFeature(monitor.Struct.hPhysicalMonitor, VCP_PROPS.INPUT_SELECT.code, ref currentValue, ref maxValue))
            {
                selectedInput = currentValue;
                return true;
            }
            return false;
        }

        public Dictionary<string, int> ReadContinuousValues(PhysicalMonitor monitor, IEnumerable<string> propertyNames)
        {
            var result = new Dictionary<string, int>();

            foreach (var propName in propertyNames)
            {
                if (!VCP_PROPS.IndexByCodeName.TryGetValue(propName, out var vcpProp))
                    continue;

                byte current = 0;
                byte max = 0;
                if (_driver.GetVcpFeature(monitor.Struct.hPhysicalMonitor, vcpProp.code, ref current, ref max))
                {
                    result[propName] = current;
                }
            }

            return result;
        }
    }
}
