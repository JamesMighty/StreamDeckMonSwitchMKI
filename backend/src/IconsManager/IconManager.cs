using BarRaider.SdTools;
using StreamDeckMonitorSwitch.ddcmon;
using Svg;
using Svg.Transforms;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace StreamDeckMonitorSwitch
{
    public enum TimerStates
    {
        WORKING = 0x01,
        IDLE = 0x02
    }

    public class TimerState
    {
        public TimerStates CurrentStatus = TimerStates.IDLE;
    }

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

    public static class IconManager
    {

        public static readonly Dictionary<MonitorStatus.PowerStatus, Color> POWERSTATUS_COLORS = new Dictionary<MonitorStatus.PowerStatus, Color>()
        {
            { MonitorStatus.PowerStatus.OFF, Color.Red },
            { MonitorStatus.PowerStatus.ON, Color.LimeGreen },
            { MonitorStatus.PowerStatus.UNKNOWN, Color.Gray },
            { MonitorStatus.PowerStatus.SLEEP, Color.Yellow }
        };

        private static List<MonitorPluginAction> RegisteredActions { get; set; } = new List<MonitorPluginAction>();

        public static Timer AutoUpdateTimer { get; private set; }
        static IconManager()
        {
            AutoUpdateTimer = new Timer((state) => {
                if (((TimerState)state).CurrentStatus == TimerStates.IDLE)
                {
                    ((TimerState)state).CurrentStatus = TimerStates.WORKING;
                    try
                    {
                        //var stops = new Stopwatch();
                        //stops.Start();
                        UpdateIcons();
                        //stops.Stop();
                        //Logger.Instance.LogMessage(TracingLevel.DEBUG, "IconManager: Icons updated in " +  stops.ElapsedMilliseconds/1000d +  "s");
                    }
                    catch (Exception err)
                    {
                        Logger.Instance.LogMessage(TracingLevel.WARN, "IconManager: Could not update icons - " + err.ToString());
                    }
                    finally { 
                        ((TimerState)state).CurrentStatus = TimerStates.IDLE;
                    }
                }
            }, new TimerState(), 1000*5, 1000*2);
        }

        public static SvgDocument MakeIcon(MonitorPluginAction action)
        {
            if (action == null)
            {
                throw new ArgumentNullException("Argument 'action' cannot be null.");
            }

            SvgDocument icon = SvgDocument.Open("img/computer-monitor-white.svg");

            if (action.Settings.VCPActionsSettings.Count > 1)
            {
                Logger.Instance.LogMessage(TracingLevel.DEBUG, "Making multi-monitor icon");
                var background_mon = (SvgElement)icon.GetElementById("main_layer").Clone();
                background_mon.ID = "backgroound_layer";
                foreach ( var child in background_mon.Children)
                {
                    child.ID = "background_"+child.ID;
                }
                background_mon.Transforms.Add(new SvgTranslate(5, -5));
                Logger.Instance.LogMessage(TracingLevel.DEBUG, background_mon.ToString());
                background_mon.Opacity = 0.5f;
                icon.Children.Insert(1, background_mon);
            }else if (action.Settings.VCPActionsSettings.Count == 1)
            {
                Logger.Instance.LogMessage(TracingLevel.DEBUG, "Making single-monitor icon");
                var physicalMonitors = MonitorConfigurator.Monitors.Where(
                    mon => mon.model == action.Settings.VCPActionsSettings.First().Key
                    );
                if (physicalMonitors.Count() > 0) {
                    var physicalMonitor = physicalMonitors.First();

                    int monitor_width = physicalMonitor.info.monitor.right - physicalMonitor.info.monitor.left;
                    int monitor_height = physicalMonitor.info.monitor.bottom - physicalMonitor.info.monitor.top;
                    var yScaleRaw = (monitor_height / (float)monitor_width);
                    var yScale = 1.5f * yScaleRaw;

                    icon.GetElementById("main_layer").Transforms.Add(new SvgScale(1, yScale));

                    icon.GetElementById("monitor_button").Transforms = new SvgTransformCollection() {
                    new SvgTranslate(0, -(12f/yScale)+12),
                    new SvgScale(1, 1/yScale)
                };
                }
            }
            return icon;
        }

        public static void Register(MonitorPluginAction action)
        {
            if (RegisteredActions != null && !RegisteredActions.Contains(action))
            {
                action.svgIcon = MakeIcon(action);
                action.PushIcon();
                RegisteredActions.Add(action);
            }
        }

        public static void Unregister(MonitorPluginAction action)
        {
            if (RegisteredActions != null && RegisteredActions.Contains(action))
            {
                RegisteredActions.Remove(action);
            }
        }

        public static void UpdateIcons()
        {
            var monitorStates = new Dictionary<string, MonitorStatus>();
            foreach (PhysicalMonitor mon in MonitorConfigurator.Monitors.ToList())
            {
                MonitorStatus monitorStatus = new MonitorStatus();
                // Get Input selected
                byte currentValue = 0;
                byte maxValue = 0;
                var outp = MonitorConfigurator.GetVCPFeatureAndVCPFeatureReply(mon.Struct.hPhysicalMonitor, VCP_PROPS.INPUT_SELECT.code, 0, ref currentValue, ref maxValue);
                if (outp)
                {
                    monitorStatus.SelectedInput = currentValue;
                }

                byte powerMode = 0;
                byte _ = 0;
                var powerModeStatus = MonitorConfigurator.GetVCPFeatureAndVCPFeatureReply(mon.Struct.hPhysicalMonitor, VCP_PROPS.POWER_MODE.code, 0, ref powerMode, ref _);

                if (powerModeStatus)
                {
                    monitorStatus.Power = (MonitorStatus.PowerStatus)powerMode;
                }
                else
                {
                    bool isOn = false;
                    var powerStateStatus = MonitorConfigurator.GetDevicePowerState(mon.device, ref isOn);
                    if (powerStateStatus)
                    {
                        monitorStatus.Power = isOn? MonitorStatus.PowerStatus.ON : MonitorStatus.PowerStatus.OFF;
                    }
                    else
                    {
                        monitorStatus.Power = MonitorStatus.PowerStatus.UNKNOWN;
                    }

                }
                monitorStates.Add(mon.model, monitorStatus);
            }

            foreach (MonitorPluginAction action in RegisteredActions.ToList())
            {
                if (action.Settings.VCPActionsSettings.Count == 0)
                    continue;

                var finalPowerStatus = monitorStates[action.Settings.VCPActionsSettings.Keys.Aggregate((a,b) => monitorStates[a].Power > monitorStates[b].Power? a:b)];
                var monitorsSelectedAsWanted = action.Settings.VCPActionsSettings.Where(vcpAction => 
                    vcpAction.Value.Where(vcp => vcp.Key == VCP_PROPS.INPUT_SELECT.codeName && monitorStates[vcpAction.Key].SelectedInput == vcp.Value).Count() > 0
                ).Count();
                var monitorsWhereSelectionWanted = action.Settings.VCPActionsSettings.Where(vcpAction =>
                    vcpAction.Value.Where(vcp => vcp.Key == VCP_PROPS.INPUT_SELECT.codeName).Count() > 0
                ).Count();

                if (monitorsSelectedAsWanted == monitorsWhereSelectionWanted)
                    action.svgIcon.GetElementById("monitor_outline").Fill = new SvgColourServer(Color.LimeGreen);
                if (monitorsSelectedAsWanted < monitorsWhereSelectionWanted)
                    action.svgIcon.GetElementById("monitor_outline").Fill = new SvgColourServer(Color.White);
                action.svgIcon.GetElementById("monitor_button").Fill = new SvgColourServer(POWERSTATUS_COLORS[finalPowerStatus.Power]);
                

                action.PushIcon();
            }

        }
     
    }
}
