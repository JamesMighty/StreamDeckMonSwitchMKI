Add-Type -TypeDefinition @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

public static class DdcDiag
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int left, top, right, bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct PHYSICAL_MONITOR
    {
        public IntPtr hPhysicalMonitor;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szPhysicalMonitorDescription;
    }

    public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data);

    [DllImport("user32.dll")] public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);
    [DllImport("dxva2.dll")] public static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, ref uint count);
    [DllImport("dxva2.dll")] public static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, uint count, [Out] PHYSICAL_MONITOR[] monitors);
    [DllImport("dxva2.dll")] public static extern bool DestroyPhysicalMonitors(uint count, PHYSICAL_MONITOR[] monitors);
    [DllImport("dxva2.dll")] public static extern bool GetCapabilitiesStringLength(IntPtr hMonitor, ref uint len);
    [DllImport("dxva2.dll", CharSet = CharSet.Ansi)] public static extern bool CapabilitiesRequestAndCapabilitiesReply(IntPtr hMonitor, [Out] System.Text.StringBuilder buffer, uint len);

    public static void Run()
    {
        var handles = new List<IntPtr>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, delegate(IntPtr h, IntPtr hdc, ref RECT r, IntPtr d) {
            handles.Add(h);
            Console.WriteLine("HMONITOR " + h + " rect=" + r.left + "," + r.top + "-" + r.right + "," + r.bottom);
            return true;
        }, IntPtr.Zero);
        Console.WriteLine("Total HMONITORs: " + handles.Count);
        foreach (var h in handles)
        {
            uint n = 0;
            if (!GetNumberOfPhysicalMonitorsFromHMONITOR(h, ref n))
            {
                Console.WriteLine("  " + h + ": GetNumberOfPhysicalMonitors FAILED err=" + Marshal.GetLastWin32Error());
                continue;
            }
            Console.WriteLine("  " + h + ": " + n + " physical monitor(s)");
            if (n == 0) continue;
            var pms = new PHYSICAL_MONITOR[n];
            if (!GetPhysicalMonitorsFromHMONITOR(h, n, pms))
            {
                Console.WriteLine("    GetPhysicalMonitors FAILED err=" + Marshal.GetLastWin32Error());
                continue;
            }
            foreach (var pm in pms)
            {
                Console.WriteLine("    -> " + pm.szPhysicalMonitorDescription);
                uint capsLen = 0;
                if (GetCapabilitiesStringLength(pm.hPhysicalMonitor, ref capsLen) && capsLen > 0)
                {
                    Console.WriteLine("    CAPS_LEN: " + capsLen);
                    var buf = new System.Text.StringBuilder((int)capsLen);
                    if (CapabilitiesRequestAndCapabilitiesReply(pm.hPhysicalMonitor, buf, capsLen))
                    {
                        var s = buf.ToString();
                        System.IO.File.WriteAllText(Environment.GetEnvironmentVariable("TEMP") + "\\ddc-caps-raw.txt", s);
                        Console.WriteLine("    CAPS written to %TEMP%\\ddc-caps-raw.txt");
                    }
                    else
                    {
                        Console.WriteLine("    CAPS: request failed err=" + Marshal.GetLastWin32Error());
                    }
                }
                else
                {
                    Console.WriteLine("    CAPS: no length err=" + Marshal.GetLastWin32Error());
                }
            }
            DestroyPhysicalMonitors(n, pms);
        }
    }
}
"@

[DdcDiag]::Run()
