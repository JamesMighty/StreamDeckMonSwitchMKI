using System;
using System.Runtime.InteropServices;
using StreamDeckMonitorSwitch.Abstraction;

namespace StreamDeckMonitorSwitch.Abstractions
{
    /**
     * Production implementation of IDdcCiDriver using native dxva2.dll P/Invoke.
     */
    public class DdcCiDriver : IDdcCiDriver
    {
        [DllImport("Dxva2.dll", EntryPoint = "SetVCPFeature", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool NativeSetVCPFeature(IntPtr hMonitor, uint dwVCPCode, int dwNewValue);

        [DllImport("Dxva2.dll", EntryPoint = "GetVCPFeatureAndVCPFeatureReply", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool NativeGetVCPFeatureAndVCPFeatureReply(
            [In] IntPtr hMonitor, [In] uint dwVCPCode, uint pvct, ref byte currentValue, ref byte maxValue);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool NativeGetDevicePowerState(IntPtr handle, ref bool state);

        public bool GetVcpFeature(IntPtr hMonitor, uint vcpCode, ref byte currentValue, ref byte maxValue)
        {
            return NativeGetVCPFeatureAndVCPFeatureReply(hMonitor, vcpCode, 0, ref currentValue, ref maxValue);
        }

        public bool SetVcpFeature(IntPtr hMonitor, uint vcpCode, int newValue)
        {
            return NativeSetVCPFeature(hMonitor, vcpCode, newValue);
        }

        public bool GetDevicePowerState(IntPtr deviceHandle, ref bool isOn)
        {
            return NativeGetDevicePowerState(deviceHandle, ref isOn);
        }
    }
}
