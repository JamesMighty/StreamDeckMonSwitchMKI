using System;

namespace StreamDeckMonitorSwitch.Abstraction
{
    /**
     * Abstraction over the DDC/CI hardware communication layer.
     * Allows unit testing without native dxva2.dll dependencies.
     */
    public interface IDdcCiDriver
    {
        /**
         * Reads a VCP feature value from a physical monitor.
         * Returns true on success; currentValue and maxValue are populated.
         */
        bool GetVcpFeature(IntPtr hMonitor, uint vcpCode, ref byte currentValue, ref byte maxValue);

        /**
         * Writes a VCP feature value to a physical monitor.
         * Returns true on success.
         */
        bool SetVcpFeature(IntPtr hMonitor, uint vcpCode, int newValue);

        /**
         * Queries the device power state via kernel32.
         * Returns true on success; isOn is populated.
         */
        bool GetDevicePowerState(IntPtr deviceHandle, ref bool isOn);
    }
}
