using CommandLine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace StreamDeckMonitorSwitch.ddcmon
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MonitorInfo
    {
        public uint size;
        public Rect monitor;
        public Rect work;
        public uint flags;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct PHYSICAL_MONITOR
    {

        /// HANDLE->void*
        public System.IntPtr hPhysicalMonitor;

        /// WCHAR[0]
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szPhysicalMonitorDescription;
    }

    [Serializable]
    public class PhysicalMonitor
    {
        public string prot { get; private set; }
        public string type { get; private set; }
        public string model { get; private set; }
        public Dictionary<VCPProperty, List<int>> possibleVCPValues { get; private set; } = new Dictionary<VCPProperty, List<int>>();
        public string mswhql { get; private set; }
        public string mswhql_ver { get; private set; }
        public string asset_eep { get; private set; }
        public string mpu { get; private set; }
        public List<int> cmds { get; private set; } = new List<int>();

        public PHYSICAL_MONITOR Struct { get; private set;}
        public MonitorInfo info { get; private set; }

        public IntPtr device { get; private set; }

        public PhysicalMonitor(string protocolClass, string type, string model, Dictionary<VCPProperty, List<int>> possibleVCPValues, string mswhhql, List<int> cmds)
        {
            this.prot = protocolClass;
            this.type = type;
            this.model = model;
            this.possibleVCPValues = possibleVCPValues;
            this.mswhql = mswhhql;
            this.cmds = cmds;
        }

        public PhysicalMonitor()
        {
        }

        public static PhysicalMonitor fromEmumeration(char[] capabilities, uint capsLen, PHYSICAL_MONITOR fmon, MonitorInfo mi, IntPtr device)
        {
            PhysicalMonitor monitor = new PhysicalMonitor();
            monitor.Struct = fmon;
            monitor.info = mi;
            monitor.device = device;

            uint floor = 0;
            Dictionary<string, string> keyValuePairs = new Dictionary<string, string>();
            string key = "";
            string value = "";

            for (uint cadet = 0; cadet < capsLen; cadet++)
            {
                switch (capabilities[cadet])
                {
                    case '(':
                        if (floor > 1)
                        {
                            value += " "+ capabilities[cadet]+" ";
                        }
                        floor++;
                        break;
                    case ')':
                        
                        floor--;
                        switch (floor)
                        {
                            case 1:
                                keyValuePairs.Add(key, value);
                                key = "";
                                value = "";
                                break;
                            default:
                                value += " " + capabilities[cadet];
                                break;
                        }
                        break;
                    default:
                        switch (floor)
                        {
                            case 1:
                                key += capabilities[cadet];
                                break;
                            default:
                                value += capabilities[cadet];
                                break;
                        }
                        break;
                }
            }

            foreach (KeyValuePair<string, string> kvp in keyValuePairs)
            {
                switch(kvp.Key)
                {
                    case "cmds":
                        monitor.cmds.AddRange(kvp.Value.Split(' ')
                            .Select(str => int.Parse(str, System.Globalization.NumberStyles.HexNumber)));
                        break;
                    case "vcp":
                        var vcps = kvp.Value.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        var vcpFloor = 0;
                        var vcpValues = new List<int>();
                        uint lastVCPHex = 0;

                        foreach (var vcp in vcps)
                        {
                            switch (vcp)
                            {
                                case "(":
                                    vcpFloor++;
                                    continue;
                                case ")":
                                    vcpFloor--;
                                    continue;
                            }

                            var vcpHex = uint.Parse(vcp, System.Globalization.NumberStyles.HexNumber);
                            if (vcpFloor == 0)
                            {
                                if (lastVCPHex != 0)
                                {
                                    VCPProperty vcpProperty = null;
                                    if (VCP_PROPS.IndexByHexCode.ContainsKey(lastVCPHex))
                                    {
                                        vcpProperty = VCP_PROPS.IndexByHexCode[lastVCPHex];
                                    }
                                    else
                                    {
                                        vcpProperty = new VCPProperty(lastVCPHex, VCP_PROPS.UNKNOWN_CODE_NAME, VCPPropertyPerms.NOT_SET);
                                    }
                                    monitor.possibleVCPValues.Add(vcpProperty, new List<int>(vcpValues));
                                    vcpValues.Clear();
                                }
                                lastVCPHex = vcpHex;
                                continue;
                            }

                            vcpValues.Add((int)vcpHex);
                        }
                        break;
                    default:
                        monitor.GetType().GetProperty(kvp.Key)?.SetValue(monitor, kvp.Value, null);
                        break;
                }
            }

            return monitor;
        }
    }

    public static class MonitorConfigurator
    {
        public static int UPDATE_PERIOD_MS = 10 * 1000;

        public static List<PhysicalMonitor> Monitors { get; private set; } = new List<PhysicalMonitor>();
        private static List<PhysicalMonitor> LoadingMonitors { get; set; } = new List<PhysicalMonitor>();

        public delegate bool MonitorEnumDelegate(IntPtr hMonitor, IntPtr hdcMonitor, ref Rect lprcMonitor, IntPtr dwData);

        public static event Action OnUpdateMonitors;
        public static Timer UpdateMonitorsTimer { get; private set; }

        static MonitorConfigurator()
        {
            UpdateMonitors();
            UpdateMonitorsTimer = new Timer((s) => {
                UpdateMonitors();
                OnUpdateMonitors?.Invoke();
                }, 0, UPDATE_PERIOD_MS, UPDATE_PERIOD_MS);
        }

        [DllImport("Dxva2.dll", EntryPoint = "GetCapabilitiesStringLength", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetCapabilitiesStringLength(IntPtr hMonitor, ref uint pdwCapabilitiesStringLengthInCharacters);

        [DllImport("Dxva2.dll", EntryPoint = "CapabilitiesRequestAndCapabilitiesReply", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CapabilitiesRequestAndCapabilitiesReply(IntPtr hMonitor, [Out] char[] buffer, uint dwCapabilitiesStringLengthInCharacters);

        [DllImport("Dxva2.dll", EntryPoint = "GetNumberOfPhysicalMonitorsFromHMONITOR")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, ref uint pdwNumberOfPhysicalMonitors);

        [DllImport("Dxva2.dll", EntryPoint = "GetPhysicalMonitorsFromHMONITOR")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, int dwPhysicalMonitorArraySize, [Out] PHYSICAL_MONITOR[] pPhysicalMonitorArray);

        [DllImport("Dxva2.dll", EntryPoint = "SetVCPFeature", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetVCPFeature(IntPtr hMonitor, uint dwVCPCode, int dwNewValue);

        [DllImport("Dxva2.dll", EntryPoint = "GetVCPFeatureAndVCPFeatureReply", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetVCPFeatureAndVCPFeatureReply([In] IntPtr hMonitor, [In] uint dwVCPCode, uint pvct, ref byte currentValue, ref byte maxValue);

        [DllImport("gdi32.dll", EntryPoint = "DDCCIGetVCPFeature", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DDCCIGetVCPFeature([In] IntPtr hMonitor, [In] uint dwVCPCode, uint pvct, ref uint pdwCurrentValue, ref uint pdwMaximumValue);

        [DllImport("user32.dll")]
        static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumDelegate lpfnEnum, IntPtr dwData);

        [DllImport("user32.dll")]
        static extern bool GetMonitorInfo(IntPtr hmon, ref MonitorInfo mi);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetDevicePowerState(IntPtr handle, ref bool state);

        static bool MonitorEnum(IntPtr hMonitor, IntPtr hdcMonitor, ref Rect lprcMonitor, IntPtr dwData)
        {   
            uint nfmon = 0;
            MonitorInfo mi = new MonitorInfo();
            mi.size = (uint)Marshal.SizeOf(mi);
            bool success = GetMonitorInfo(hMonitor, ref mi);

            if (!GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, ref nfmon))
            {
                Console.WriteLine("Could not get number of physical monitors, skipping..");
                return true;
            }

#if DEBUG
            if (nfmon == 0)
            {
                Console.WriteLine("No physical monitors found for handle {0}", hMonitor);
                return true;
            } else
            {
                Console.WriteLine("Found {0} physical monitors for handle {1}", nfmon, hMonitor);
            }
#endif

            PHYSICAL_MONITOR[] fMonitor = ArrayPool<PHYSICAL_MONITOR>.Shared.Rent((int)(nfmon));
            if(!GetPhysicalMonitorsFromHMONITOR(hMonitor, 1, fMonitor))
            {
                Console.WriteLine("Could not get physical monitor struct from monitor handle");
                return true;
            }

            for (int i = 0; i < nfmon; i++)
            {
                uint capsLen = 0;

                if (!GetCapabilitiesStringLength(fMonitor[i].hPhysicalMonitor, ref capsLen)) {
                    Console.WriteLine("Could not get capabilities string lenght");
                    continue;
                }
#if DEBUG
                Console.WriteLine("Caps len: {0}", capsLen);
#endif
                char[] buffer = ArrayPool<char>.Shared.Rent((int)(capsLen));

                if (!CapabilitiesRequestAndCapabilitiesReply(fMonitor[i].hPhysicalMonitor, buffer, capsLen)) {
                    Console.WriteLine("Could not get capabilities string");
                    continue;
                }
#if DEBUG
                Console.WriteLine(buffer);
#endif
                var physicalMon = PhysicalMonitor.fromEmumeration(buffer, capsLen, fMonitor[i], mi, hMonitor);
                LoadingMonitors.Add(physicalMon);
#if DEBUG
                Console.WriteLine(JObject.FromObject(physicalMon).ToString());
                foreach(var kv in physicalMon.possibleVCPValues)
                {
                    Console.WriteLine(JObject.FromObject(kv.Key).ToString()+" :: ["+String.Join(", ", kv.Value)+"]\n");
                }
#endif

            }
#if DEBUG
            Console.WriteLine("---");
#endif

            return true;
        }

        public static void UpdateMonitors()
        {
            LoadingMonitors.Clear();
            MonitorEnumDelegate med = new MonitorEnumDelegate(MonitorEnum);
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, med, IntPtr.Zero);
            Monitors = new List<PhysicalMonitor>(LoadingMonitors);
        }

        [STAThread]
        public static void Main(string[] args)
        { 
            MonitorEnumDelegate med = new MonitorEnumDelegate(MonitorEnum);
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, med, IntPtr.Zero);
            Console.ReadKey();
        }

    }
}
