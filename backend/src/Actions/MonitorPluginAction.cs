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

            // The PI persists checkbox selections as arrays of strings
            // (e.g. { "MODEL-A": { "Input Select": ["15", "17"] } }), so accept
            // lenient shapes; single scalars are wrapped into one-element lists.
            [JsonProperty("values")]
            [JsonConverter(typeof(VcpActionsSettingsConverter))]
            public Dictionary<string, Dictionary<string, List<int>>> VCPActionsSettings { get; private set; } = new Dictionary<string, Dictionary<string, List<int>>>();

            // How a key press applies the configured values:
            //   set       - write the first selected value (discrete) or the target value (continuous)
            //   toggle    - switch between the two selected values (discrete)
            //   loop      - cycle through all selected values in order (discrete)
            //   scrollUp  - increase a continuous value by stepSize
            //   scrollDown- decrease a continuous value by stepSize
            [JsonProperty("actionMode")]
            public string ActionMode { get; private set; } = "set";

            // Step size used by the scrollUp / scrollDown modes on continuous controls.
            [JsonProperty("stepSize")]
            public int StepSize { get; private set; } = 1;

            // When true, scrolling past the max/min wraps around to the other end.
            [JsonProperty("wrapAround")]
            public bool WrapAround { get; private set; } = false;

            // Target value used by the "set" mode on continuous controls.
            [JsonProperty("targetValue")]
            public int TargetValue { get; private set; } = 50;

            /**
             * Guarantees non-null collections after deserialization, since any of the
             * keys may be absent from a (partial) settings payload.
             */
            public void EnsureDefaults()
            {
                if (Monitors == null) Monitors = new List<string>();
                if (Properties == null) Properties = new Dictionary<string, List<string>>();
                if (VCPActionsSettings == null) VCPActionsSettings = new Dictionary<string, Dictionary<string, List<int>>>();
                if (ActionMode == null) ActionMode = "set";
                if (StepSize < 1) StepSize = 1;
            }
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
                    this.Settings.EnsureDefaults();
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
            if (!IsChangingVCP && Settings.Properties.Count > 0)
            {
                IsChangingVCP = true;
                Logger.Instance.LogMessage(TracingLevel.INFO, "Key Pressed");
                svgIcon.GetElementById("monitor_outline").Fill = new SvgColourServer(Color.Orange);
                PushIcon();

                foreach (var monitorEntry in Settings.Properties)
                {
                    var monitorModel = monitorEntry.Key;
                    var monitorSearch = MonitorConfigurator.Monitors.Where(mon => mon.model == monitorModel);

                    // DDC/CI contention can transiently empty the monitor list;
                    // re-enumerate once before giving up so a key press self-heals.
                    if (!monitorSearch.Any())
                    {
                        MonitorConfigurator.UpdateMonitors();
                        monitorSearch = MonitorConfigurator.Monitors.Where(mon => mon.model == monitorModel);
                    }

                    if (!monitorSearch.Any())
                    {
                        SetFeedbackColor(false);
                        continue;
                    }

                    var hMonitor = monitorSearch.First().Struct.hPhysicalMonitor;

                    foreach (var propertyName in monitorEntry.Value)
                    {
                        var success = ApplyVcpAction(hMonitor, monitorModel, propertyName);
                        SetFeedbackColor(success);
                    }
                }
            }
        }

        /**
         * Applies the configured action for a single VCP property on a monitor.
         * Returns true on success.
         */
        private bool ApplyVcpAction(IntPtr hMonitor, string monitorModel, string propertyName)
        {
            if (!VCP_PROPS.IndexByCodeName.TryGetValue(propertyName, out var vcpProperty))
            {
                Logger.Instance.LogMessage(TracingLevel.WARN,
                    "KeyPressed: unknown VCP property name in settings: " + propertyName);
                return false;
            }

            // Check if this property has discrete values configured.
            List<int> discreteValues = null;
            var hasDiscreteValues = Settings.VCPActionsSettings.TryGetValue(monitorModel, out var monitorValues)
                && monitorValues.TryGetValue(propertyName, out discreteValues)
                && discreteValues != null && discreteValues.Count > 0;

            if (hasDiscreteValues)
            {
                // Discrete control: use toggle/loop/set logic.
                int targetValue;
                if (!TryResolveTargetValue(hMonitor, vcpProperty.code, discreteValues, Settings.ActionMode, out targetValue))
                {
                    return false;
                }
                return MonitorConfigurator.SetVCPFeature(hMonitor, vcpProperty.code, targetValue);
            }

            // Continuous control: use scroll or target-value logic.
            return ApplyContinuousAction(hMonitor, vcpProperty, propertyName);
        }

        /**
         * Handles continuous (scrollable) VCP controls.
         */
        private bool ApplyContinuousAction(IntPtr hMonitor, VCPProperty vcpProperty, string propertyName)
        {
            var mode = Settings.ActionMode;

            if (mode == ActionModes.SET)
            {
                // Set to target value.
                return MonitorConfigurator.SetVCPFeature(hMonitor, vcpProperty.code, Settings.TargetValue);
            }

            if (mode == ActionModes.SCROLL_UP || mode == ActionModes.SCROLL_DOWN)
            {
                // Read current value and compute next.
                byte currentValue = 0;
                byte maxValue = 0;
                if (!MonitorConfigurator.GetVCPFeatureAndVCPFeatureReply(hMonitor, vcpProperty.code, 0, ref currentValue, ref maxValue))
                {
                    Logger.Instance.LogMessage(TracingLevel.WARN,
                        "KeyPressed: could not read current value for continuous VCP 0x" + vcpProperty.code.ToString("X2"));
                    return false;
                }

                var nextValue = ActionModes.ResolveScrollValue(
                    currentValue, Settings.StepSize, 0, maxValue, mode, Settings.WrapAround);

                return MonitorConfigurator.SetVCPFeature(hMonitor, vcpProperty.code, nextValue);
            }

            // Unsupported mode for continuous controls.
            Logger.Instance.LogMessage(TracingLevel.WARN,
                "KeyPressed: unsupported mode '" + mode + "' for continuous property: " + propertyName);
            return false;
        }

        private void SetFeedbackColor(bool success)
        {
            svgIcon.GetElementById("monitor_outline").Fill = new SvgColourServer(success ? Color.Yellow : Color.Red);
            PushIcon();
        }

        /**
         * Resolves the value to write on a key press, based on the configured
         * action mode:
         *   set    - the first configured value
         *   toggle - the configured value that differs from the current one
         *   loop   - the configured value following the current one (wrapping)
         * When the current value cannot be read (or is not among the configured
         * values), the first configured value is used.
         */
        private bool TryResolveTargetValue(IntPtr hMonitor, uint vcpCode, List<int> values, string mode, out int targetValue)
        {
            targetValue = values[0];

            if (mode != "toggle" && mode != "loop")
            {
                return true;
            }

            byte currentValue = 0;
            byte maxValue = 0;
            if (!MonitorConfigurator.GetVCPFeatureAndVCPFeatureReply(hMonitor, vcpCode, 0, ref currentValue, ref maxValue))
            {
                Logger.Instance.LogMessage(TracingLevel.WARN,
                    "KeyPressed: could not read current value for VCP 0x" + vcpCode.ToString("X2")
                    + "; falling back to first configured value");
                return true;
            }

            targetValue = ActionModes.ResolveNextValue(values, currentValue, mode);
            return true;
        }

        public override void KeyReleased(KeyPayload payload) {
            IsChangingVCP = false;

            // DDC/CI is a polling protocol — the monitor cannot push state changes.
            // Since we caused the change, do an immediate on-demand read shortly
            // after release so the icon reflects the new state without waiting for
            // the periodic timer.
            _ = Task.Delay(1500).ContinueWith(t =>
            {
                try
                {
                    // Skip when a newer press is already in flight.
                    if (!IsChangingVCP)
                    {
                        IconManager.RefreshActionIcon(this);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Instance.LogMessage(TracingLevel.WARN,
                        "KeyReleased: could not refresh icon - " + ex.ToString());
                }
            });
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
                    newSettings.EnsureDefaults();
                    var todo = new List<Action>();

                    // The icon geometry (monitor count) and the VCP glyph (property
                    // name) both live in the rendered SVG, so rebuild on either change.
                    var monitorsChanged = Settings.Monitors.Count != newSettings.Monitors.Count;
                    var propertiesChanged = !Settings.Properties
                        .SelectMany(kv => kv.Value)
                        .OrderBy(x => x)
                        .SequenceEqual(newSettings.Properties.SelectMany(kv => kv.Value).OrderBy(x => x));

                    if (monitorsChanged || propertiesChanged)
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

        // Last pushed inspector payloads, so the settings-driven cascade does not
        // re-push (and the PI does not re-render) data that has not changed.
        // Direct PI requests (sendToPlugin) always bypass this dedup.
        private string lastMonitorsPayload;
        private string lastPropertiesPayload;
        private string lastValuesPayload;

        private void UpdateInspectorMonitors(bool force = true)
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
            var serialized = monitorspayload.ToString(Formatting.None);

            if (!force && serialized == lastMonitorsPayload)
            {
                Logger.Instance.LogMessage(TracingLevel.DEBUG, "monitors unchanged, skipping push");
                return;
            }

            lastMonitorsPayload = serialized;
            Connection.SendToPropertyInspectorAsync(monitorspayload);

            Logger.Instance.LogMessage(TracingLevel.DEBUG, monitorspayload.ToString());
        }

        private void UpdateInspectorProperties(bool force = true)
        {
            Logger.Instance.LogMessage(TracingLevel.DEBUG, "Getting properties ..");

            var propertiesPayloadRaw = MonitorConfigurator.Monitors
                .Where(monitor => Settings.Monitors.Contains(monitor.model))
                .Select(monitor =>
                        new InspectorDataSourceItemGroupDTO(monitor.model,
                        monitor.possibleVCPValues
                            .Where(vcp => vcp.Key.codeName != VCP_PROPS.UNKNOWN_CODE_NAME)
                            .Where(vcp => vcp.Key.perms.HasFlag(VCPPropertyPerms.WRITE))
                            .Select(vcp => new InspectorDataSourceItemDTO(vcp.Key.codeName, vcp.Key.codeName))
                            .ToList<IInspectorDataSourceItemDTO>())
                ).ToList<IInspectorDataSourceItemDTO>();

            if (propertiesPayloadRaw.Count == 0)
            {
                Logger.Instance.LogMessage(TracingLevel.DEBUG, "no properties found");
                return;
            };

            var propertiesPayload = JObject.FromObject(new InspectorDataSourceDTO("getProperties", propertiesPayloadRaw));
            var serialized = propertiesPayload.ToString(Formatting.None);

            if (!force && serialized == lastPropertiesPayload)
            {
                Logger.Instance.LogMessage(TracingLevel.DEBUG, "properties unchanged, skipping push");
                return;
            }

            lastPropertiesPayload = serialized;
            Connection.SendToPropertyInspectorAsync(propertiesPayload);

            Logger.Instance.LogMessage(TracingLevel.DEBUG, propertiesPayload.ToString());
        }

        private void UpdateInspectorValues(bool force = true)
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
                            .Where(vcp => Settings.Properties.TryGetValue(monitor.model, out var selectedProps) && selectedProps.Contains(vcp.Key.codeName))
                            .Where(vcp => vcp.Value.Count > 0)
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
            var serialized = valuesPayload.ToString(Formatting.None);

            if (!force && serialized == lastValuesPayload)
            {
                Logger.Instance.LogMessage(TracingLevel.DEBUG, "values unchanged, skipping push");
                return;
            }

            lastValuesPayload = serialized;
            Connection.SendToPropertyInspectorAsync(valuesPayload);

            Logger.Instance.LogMessage(TracingLevel.DEBUG, valuesPayload.ToString());
        }

        /**
         * Settings-driven cascade: pushes only what actually changed, so ticking
         * a value checkbox does not re-render the whole inspector (monitors,
         * properties, values). Direct PI requests always force a reply.
         */
        private void UpdateInspector()
        {
            Logger.Instance.LogMessage(TracingLevel.DEBUG, "Updating inspector ..");
            UpdateInspectorMonitors(force: false);

            if (Settings.Monitors.Count == 0) return;
            UpdateInspectorProperties(force: false);

            if (Settings.Properties.Count == 0) return;
            UpdateInspectorValues(force: false);
        }

        /**
         * Sends VCP property type info to the PI so it can show the correct
         * action-mode options and step-size controls for continuous properties.
         */
        private void SendVcpInfo()
        {
            Logger.Instance.LogMessage(TracingLevel.DEBUG, "Getting VCP info ..");

            var infoItems = new JArray();

            foreach (var monitorEntry in Settings.Properties)
            {
                var monitorModel = monitorEntry.Key;
                var monitor = MonitorConfigurator.Monitors.FirstOrDefault(m => m.model == monitorModel);
                if (monitor == null) continue;

                foreach (var propertyName in monitorEntry.Value)
                {
                    var hasDiscreteValues = monitor.possibleVCPValues
                        .Any(kv => kv.Key.codeName == propertyName && kv.Value.Count > 0);

                    var item = new JObject
                    {
                        ["monitor"] = monitorModel,
                        ["property"] = propertyName,
                        ["type"] = hasDiscreteValues ? "discrete" : "continuous"
                    };

                    // For continuous controls, read the max value from the monitor.
                    if (!hasDiscreteValues && VCP_PROPS.IndexByCodeName.TryGetValue(propertyName, out var vcpProp))
                    {
                        byte current = 0;
                        byte max = 0;
                        if (MonitorConfigurator.GetVCPFeatureAndVCPFeatureReply(
                            monitor.Struct.hPhysicalMonitor, vcpProp.code, 0, ref current, ref max))
                        {
                            item["maxValue"] = max;
                        }
                    }

                    infoItems.Add(item);
                }
            }

            var payload = new JObject
            {
                ["event"] = "getVcpInfo",
                ["items"] = infoItems
            };

            Connection.SendToPropertyInspectorAsync(payload);
            Logger.Instance.LogMessage(TracingLevel.DEBUG, payload.ToString());
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
                        // Properties only exist for selected monitors; before that the PI
                        // stays pending and is answered once settings arrive (UpdateInspector).
                        if (Settings.Monitors.Count == 0) break;

                        UpdateInspectorProperties();
                        break;
                    case "getValues":
                        // Values only exist for selected monitors AND properties.
                        if (Settings.Monitors.Count == 0 || Settings.Properties.Count == 0) break;

                        UpdateInspectorValues();
                        break;
                    case "getVcpInfo":
                        // Property type info (continuous vs discrete, max value).
                        if (Settings.Monitors.Count == 0 || Settings.Properties.Count == 0) break;

                        SendVcpInfo();
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