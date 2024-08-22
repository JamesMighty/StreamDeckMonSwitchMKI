using BarRaider.SdTools;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using StreamDeckMonitorSwitch.ddcmon;
using StreamDeckMonitorSwitch.dtos;
using Svg;
using System.Drawing;

namespace StreamDeckMonitorSwitch
{

    [PluginActionId("com.elgato.streamdeck.streamdeckmonswitch.monitorpluginaction")]
    public class MonitorPluginAction : KeypadBase
    {
        public SvgDocument svgIcon { get; internal set; }

        public bool IsChangingVCP { get; private set; }
        public PluginSettings Settings { get; private set; }

        public string ContextId
        {
            get
            {
                return Connection.ContextId;
            }
        }


        public class PluginSettings
        {
            public static PluginSettings CreateDefaultSettings()
            {
                PluginSettings instance = new PluginSettings();
                return instance;
            }

            [JsonProperty("monitors")]
            public List<string> Monitors { get; private set; } = new List<string>();

            [JsonProperty("properties")]
            public Dictionary<string, List<string>> Properties { get; private set; } = new Dictionary<string, List<string>>();

            [JsonProperty("values", DefaultValueHandling = DefaultValueHandling.Populate)]
            public Dictionary<string, Dictionary<string, int>> VCPActionsSettings { get; private set; } = new Dictionary<string, Dictionary<string, int>>();
        }

        public MonitorPluginAction(SDConnection connection, InitialPayload payload) : base(connection, payload)
        {
            Logger.Instance.LogMessage(
                TracingLevel.DEBUG,
                "Initial settings payload:\n" + JObject.FromObject(payload).ToString()
                );

            this.Settings = PluginSettings.CreateDefaultSettings();

            if (payload.Settings == null || payload.Settings.Count == 0)
            {
                SaveSettings();
            }
            else
            {
                try
                {
                    this.Settings = payload.Settings.ToObject<PluginSettings>();
                    Logger.Instance.LogMessage(
                        TracingLevel.DEBUG,
                        "Initial loaded settings:\n" + JObject.FromObject(Settings).ToString()
                        );
                }
                catch (Exception ex)
                {
                    Logger.Instance.LogMessage(TracingLevel.WARN,
                        "Could not load persistent settings from object:\n "
                        + payload.Settings.ToString(Formatting.Indented)
                        + "\n got error:\n"
                        + ex.ToString());
                }
            }

            IconManager.Register(this);

            Connection.OnSendToPlugin += Connection_OnSendToPlugin;
        }


        public void PushIcon()
        {
            Connection.SetImageAsync(svgIcon.Draw(128, 128));
        }

        #region Override Methods

        public override void Dispose()
        {
            Logger.Instance.LogMessage(TracingLevel.INFO, $"Destructor called");
            IconManager.Unregister(this);
        }

        public override void KeyPressed(KeyPayload payload)
        {
            if (!IsChangingVCP && Settings.VCPActionsSettings.Count > 0 && Settings.VCPActionsSettings.All(vcpaction => vcpaction.Value.Count > 0))
            {
                IsChangingVCP = true;
                Logger.Instance.LogMessage(TracingLevel.INFO, "Key Pressed");
                svgIcon.GetElementById("monitor_outline").Fill = new SvgColourServer(Color.Orange);
                PushIcon();

                foreach (var action in Settings.VCPActionsSettings)
                {
                    foreach (var vcpSetting in action.Value)
                    {
                        var success = true;
                        var monitorSearch = MonitorConfigurator.Monitors.Where(mon => mon.model == action.Key);
                        if (!monitorSearch.Any())
                        {
                            success = false;
                        }
                        else
                        {
                            success = MonitorConfigurator.SetVCPFeature(
                                monitorSearch.First().Struct.hPhysicalMonitor,
                                VCP_PROPS.IndexByCodeName[vcpSetting.Key].code,
                                vcpSetting.Value);
                        }

                        if (success)
                        {
                            svgIcon.GetElementById("monitor_outline").Fill = new SvgColourServer(Color.Yellow);
                        }else
                        {
                            svgIcon.GetElementById("monitor_outline").Fill = new SvgColourServer(Color.Red);
                        }
                        PushIcon();
                    }

                }
            }
        }

        public override void KeyReleased(KeyPayload payload) {
            IsChangingVCP = false;
        }

        public override void OnTick() { }

        public override void ReceivedSettings(ReceivedSettingsPayload payload)
        {
            Logger.Instance.LogMessage(TracingLevel.DEBUG, "ReceivedSettings Payload:\n" + JsonConvert.SerializeObject(payload, Formatting.Indented));

            if (payload.Settings != null)
            {
                try
                {
                    var newSettings = payload.Settings.ToObject<PluginSettings>(new JsonSerializer());
                    var todo = new List<Action>();


                    if (Settings.Monitors.Count != newSettings.Monitors.Count)
                    {
                        todo.Add(() =>
                        {
                            svgIcon = IconManager.MakeIcon(this);
                            PushIcon();
                        });
                    }

                    Settings = newSettings;

                    Logger.Instance.LogMessage(TracingLevel.DEBUG,
                        "New set settings:\n"
                        + JObject.FromObject(Settings).ToString());

                    UpdateInspector();
                    todo.ForEach(action => action());
                } catch (Exception ex)
                {
                    Logger.Instance.LogMessage(TracingLevel.WARN,
                        "::ReceivedSettings - Could not read settings from object:\n"
                        + payload.Settings.ToString(Formatting.Indented)
                        + "\n with exception:'\n"
                        + ex.ToString());
                }
            }

        }

        public override void ReceivedGlobalSettings(ReceivedGlobalSettingsPayload payload) {
            Logger.Instance.LogMessage(TracingLevel.DEBUG,
                "::ReceivedGlobalSettings - not implemented ... skipping ");
        }

        #endregion

        #region Private Methods

        private Task SaveSettings()
        {
            return Connection.SetSettingsAsync(JObject.FromObject(Settings));
        }

        private void UpdateInspectorMonitors()
        {
            Logger.Instance.LogMessage(TracingLevel.DEBUG, "Getting monitors ..");

            var monitorsPayloadRaw = MonitorConfigurator.Monitors.Select(monitor =>
                new InspectorDataSourceItemDTO(monitor.model, monitor.model)
                ).ToList<IInspectorDataSourceItemDTO>();

            if (monitorsPayloadRaw.Count == 0)
            {
                Logger.Instance.LogMessage(TracingLevel.DEBUG, "no monitors found");
                return;
            };

            var monitorspayload = JObject.FromObject(new InspectorDataSourceDTO("getMonitors", monitorsPayloadRaw));
            Connection.SendToPropertyInspectorAsync(monitorspayload);

            Logger.Instance.LogMessage(TracingLevel.DEBUG, monitorspayload.ToString());
        }

        private void UpdateInspectorProperties()
        {
            Logger.Instance.LogMessage(TracingLevel.DEBUG, "Getting properties ..");

            var propertiesPayloadRaw = MonitorConfigurator.Monitors
                .Where(monitor => Settings.Monitors.Contains(monitor.model))
                .Select(monitor =>
                        new InspectorDataSourceItemGroupDTO(monitor.model,
                        monitor.possibleVCPValues
                            .Where(vcp => vcp.Key.codeName != VCP_PROPS.UNKNOWN_CODE_NAME)
                            .Where(vcp => vcp.Key.perms.HasFlag(VCPPropertyPerms.WRITE))
                            .Where(vcp => monitor.possibleVCPValues[vcp.Key].Count > 0)
                            .Select(vcp => new InspectorDataSourceItemDTO(vcp.Key.codeName, vcp.Key.codeName))
                            .ToList<IInspectorDataSourceItemDTO>())
                ).ToList<IInspectorDataSourceItemDTO>();

            if (propertiesPayloadRaw.Count == 0)
            {
                Logger.Instance.LogMessage(TracingLevel.DEBUG, "no properties found");
                return;
            };

            var propertiesPayload = JObject.FromObject(new InspectorDataSourceDTO("getProperties", propertiesPayloadRaw));
            Connection.SendToPropertyInspectorAsync(propertiesPayload);

            Logger.Instance.LogMessage(TracingLevel.DEBUG, propertiesPayload.ToString());
        }

        private void UpdateInspectorValues()
        {
            Logger.Instance.LogMessage(TracingLevel.DEBUG, "Getting values ..");
            var valuesPayloadRaw = MonitorConfigurator.Monitors
                .Where(monitor => Settings.Monitors.Contains(monitor.model))
                .Select(monitor =>
                    new InspectorDataSourceItemGroupDTO(
                        monitor.model,
                        monitor.possibleVCPValues
                            .Where(vcp => vcp.Key.codeName != VCP_PROPS.UNKNOWN_CODE_NAME)
                            .Where(vcp => vcp.Key.perms.HasFlag(VCPPropertyPerms.WRITE))
                            .Where(vcp => Settings.Properties[monitor.model].Contains(vcp.Key.codeName))
                            .Select(vcp =>
                                new InspectorDataSourceItemGroupDTO(
                                    vcp.Key.codeName,
                                    vcp.Value.Select(vcpValue =>
                                        new InspectorDataSourceItemDTO(vcpValue.ToString(), vcpValue.ToString())
                                        ).ToList<IInspectorDataSourceItemDTO>()
                                )
                            ).ToList<IInspectorDataSourceItemDTO>()
                        )
                    ).ToList<IInspectorDataSourceItemDTO>();

            if (valuesPayloadRaw.Count == 0)
            {
                Logger.Instance.LogMessage(TracingLevel.DEBUG, "no values found");
                return;
            }

            var valuesPayload = JObject.FromObject(new InspectorDataSourceDTO("getValues", valuesPayloadRaw));
            Connection.SendToPropertyInspectorAsync(valuesPayload);

            Logger.Instance.LogMessage(TracingLevel.DEBUG, valuesPayload.ToString());
        }

        private void UpdateInspector()
        {
            Logger.Instance.LogMessage(TracingLevel.DEBUG, "Updating inspector ..");
            UpdateInspectorMonitors();

            if (Settings.Monitors.Count == 0) return;
            UpdateInspectorProperties();

            if (Settings.Properties.Count == 0) return;
            UpdateInspectorValues();
        }

        private void Connection_OnSendToPlugin(object sender, BarRaider.SdTools.Wrappers.SDEventReceivedEventArgs<BarRaider.SdTools.Events.SendToPlugin> e)
        {
            var payload = e.Event.Payload;

            Logger.Instance.LogMessage(TracingLevel.DEBUG,
                "OnSendToPlugin Payload:\n"
                + JsonConvert.SerializeObject(payload));

            if (payload.ContainsKey("event"))
            {
                switch (payload.GetValue("event").Value<string>())
                {
                    case "getMonitors":
                        UpdateInspectorMonitors();
                        break;
                    case "getProperties":
                        if (Settings.VCPActionsSettings.Count == 0) break;

                        UpdateInspectorProperties();
                        break;
                    case "getValues":
                        if (Settings.VCPActionsSettings.Count == 0) break;

                        UpdateInspectorValues();
                        break;
                    default:
                        Logger.Instance.LogMessage(TracingLevel.WARN,
                            "Got unknown settings:\n"
                            + JsonConvert.SerializeObject(payload));
                        break;
                }
            }
        }

        #endregion
    }
}