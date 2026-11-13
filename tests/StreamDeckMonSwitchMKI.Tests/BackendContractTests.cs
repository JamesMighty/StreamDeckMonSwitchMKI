using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using StreamDeckMonitorSwitch;
using StreamDeckMonitorSwitch.ddcmon;
using StreamDeckMonitorSwitch.dtos;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace StreamDeckMonSwitch.Tests
{
    /**
     * <summary>
     * In-process tests covering the backend's core contracts:
     * DDC/CI capabilities-string parsing, the VCP registry, the settings
     * schema shared with the Property Inspector, and the inspector DTOs.
     * </summary>
     */
    [TestFixture]
    public class BackendContractTests
    {
        private const string SampleCaps =
            "(prot(monitor)type(lcd)model(E2E-FAKE-MONITOR)cmds(01 02 03 07 0C E3 F3)" +
            "vcp(02 04 05 08 0B 0C(04 05 06 08 0B) 10(00 64) 12(00 64) 14(04 05 06 08 0B 0C) " +
            "16(00 64) 18(00 64) 1A(00 64) 52 60(0F 10 11 12) AC AE B2 B6 C0 C6 C8 C9 CA(01 02) CC(01 02 03) D6(01 04 05)))";

        private static PhysicalMonitor ParseSample()
        {
            var chars = SampleCaps.ToCharArray();
            var fmon = new PHYSICAL_MONITOR { hPhysicalMonitor = System.IntPtr.Zero, szPhysicalMonitorDescription = "e2e" };
            var mi = new MonitorInfo();
            return PhysicalMonitor.fromEmumeration(chars, (uint)chars.Length, fmon, mi, System.IntPtr.Zero);
        }

        [Test]
        public void Capabilities_Parsing_Extracts_TopLevel_Fields()
        {
            var monitor = ParseSample();

            Assert.AreEqual("monitor", monitor.prot);
            Assert.AreEqual("lcd", monitor.type);
            Assert.AreEqual("E2E-FAKE-MONITOR", monitor.model);
        }

        [Test]
        public void Capabilities_Parsing_Extracts_Commands()
        {
            var monitor = ParseSample();

            CollectionAssert.AreEquivalent(new List<int> { 0x01, 0x02, 0x03, 0x07, 0x0C, 0xE3, 0xF3 }, monitor.cmds);
        }

        [Test]
        public void Capabilities_Parsing_Maps_Known_Vcp_Codes_With_Values()
        {
            var monitor = ParseSample();

            var byCode = monitor.possibleVCPValues.ToDictionary(kv => kv.Key.code, kv => kv.Value);

            // Input Select with its advertised values.
            CollectionAssert.AreEquivalent(new List<int> { 0x0F, 0x10, 0x11, 0x12 }, byCode[0x60]);

            // Brightness range 0..100.
            CollectionAssert.AreEquivalent(new List<int> { 0x00, 0x64 }, byCode[0x10]);

            // Power mode.
            CollectionAssert.AreEquivalent(new List<int> { 0x01, 0x04, 0x05 }, byCode[0xD6]);
        }

        [Test]
        public void Capabilities_Parsing_Includes_Trailing_Vcp_Code()
        {
            // Regression: the trailing VCP code (here D6) used to be dropped,
            // which silently removed power-mode support from the icon polling.
            var monitor = ParseSample();

            Assert.IsTrue(monitor.possibleVCPValues.ContainsKey(VCP_PROPS.POWER_MODE),
                "trailing VCP code D6 (Power mode) must be parsed");
        }

        [Test]
        public void Capabilities_Parsing_Marks_Valueless_Codes()
        {
            var monitor = ParseSample();

            // 0x52 (Active Control) is advertised without a value list.
            var activeControl = monitor.possibleVCPValues
                .FirstOrDefault(kv => kv.Key.code == 0x52);

            Assert.NotNull(activeControl.Key, "valueless VCP code should still be recorded");
            Assert.IsEmpty(activeControl.Value);
        }

        [Test]
        public void VcpRegistry_Indexes_Are_Consistent()
        {
            Assert.AreEqual(VCP_PROPS.List.Count, VCP_PROPS.IndexByHexCode.Count);
            Assert.AreEqual(VCP_PROPS.List.Count, VCP_PROPS.IndexByCodeName.Count);

            Assert.AreEqual(0x60u, VCP_PROPS.IndexByCodeName["Input Select"].code);
            Assert.AreEqual("Input Select", VCP_PROPS.IndexByHexCode[0x60].codeName);
            Assert.IsTrue(VCP_PROPS.INPUT_SELECT.perms.HasFlag(VCPPropertyPerms.WRITE));
        }

        [Test]
        public void PluginSettings_Accepts_PropertyInspector_Shapes()
        {
            // Exactly what the PI checkbox-lists persist: flat list, per-model
            // property lists, and string arrays for values.
            var json = @"{
                ""monitors"": [""E2E-FAKE-MONITOR""],
                ""properties"": { ""E2E-FAKE-MONITOR"": [""Input Select""] },
                ""values"": { ""E2E-FAKE-MONITOR"": { ""Input Select"": [""15""] } }
            }";

            var settings = JsonConvert.DeserializeObject<MonitorPluginAction.PluginSettings>(json);
            settings.EnsureDefaults();

            CollectionAssert.AreEquivalent(new[] { "E2E-FAKE-MONITOR" }, settings.Monitors);
            CollectionAssert.AreEquivalent(new[] { "Input Select" }, settings.Properties["E2E-FAKE-MONITOR"]);
            CollectionAssert.AreEquivalent(new[] { 15 }, settings.VCPActionsSettings["E2E-FAKE-MONITOR"]["Input Select"]);
        }

        [TestCase("15", new[] { 15 })]
        [TestCase("[15]", new[] { 15 })]
        [TestCase("[\"15\"]", new[] { 15 })]
        [TestCase("[1, 15]", new[] { 1, 15 })]
        [TestCase("[\"15\", \"17\"]", new[] { 15, 17 })]
        public void PluginSettings_Coerces_Vcp_Value_Shapes(string valueJson, int[] expected)
        {
            var json = @"{ ""monitors"": [], ""properties"": {}, ""values"": { ""M"": { ""Input Select"": " + valueJson + @" } } }";

            var settings = JsonConvert.DeserializeObject<MonitorPluginAction.PluginSettings>(json);

            CollectionAssert.AreEqual(expected, settings.VCPActionsSettings["M"]["Input Select"]);
        }

        [Test]
        public void PluginSettings_Tolerates_Missing_Keys()
        {
            var settings = JsonConvert.DeserializeObject<MonitorPluginAction.PluginSettings>("{}");
            settings.EnsureDefaults();

            Assert.NotNull(settings.Monitors);
            Assert.NotNull(settings.Properties);
            Assert.NotNull(settings.VCPActionsSettings);
            Assert.AreEqual("set", settings.ActionMode);
        }

        [TestCase("set")]
        [TestCase("toggle")]
        [TestCase("loop")]
        public void PluginSettings_Accepts_ActionMode(string mode)
        {
            var json = @"{ ""monitors"": [], ""properties"": {}, ""values"": {}, ""actionMode"": """ + mode + @""" }";

            var settings = JsonConvert.DeserializeObject<MonitorPluginAction.PluginSettings>(json);
            settings.EnsureDefaults();

            Assert.AreEqual(mode, settings.ActionMode);
        }

        [Test]
        public void PluginSettings_Serializes_Back_To_Pi_Shape()
        {
            var json = @"{ ""monitors"": [""M""], ""properties"": { ""M"": [""Brightness""] }, ""values"": { ""M"": { ""Brightness"": [""64""] } } }";
            var settings = JsonConvert.DeserializeObject<MonitorPluginAction.PluginSettings>(json);
            settings.EnsureDefaults();

            var roundTrip = JObject.FromObject(settings);

            Assert.AreEqual("monitors", ((JProperty)roundTrip.Properties().First()).Name);
            CollectionAssert.AreEquivalent(new[] { "M" }, roundTrip["monitors"].ToObject<List<string>>());
            CollectionAssert.AreEqual(new[] { 64 }, roundTrip["values"]["M"]["Brightness"].ToObject<List<int>>());
        }

        [Test]
        public void ResolveNextValue_Set_Mode_Always_Returns_First()
        {
            var values = new List<int> { 15, 17, 18 };

            Assert.AreEqual(15, ActionModes.ResolveNextValue(values, 15, "set"));
            Assert.AreEqual(15, ActionModes.ResolveNextValue(values, 17, "set"));
            Assert.AreEqual(15, ActionModes.ResolveNextValue(values, 99, "set"));
            Assert.AreEqual(15, ActionModes.ResolveNextValue(values, 17, null));
        }

        [Test]
        public void ResolveNextValue_Toggle_Switches_Between_Values()
        {
            var values = new List<int> { 15, 17 };

            Assert.AreEqual(17, ActionModes.ResolveNextValue(values, 15, "toggle"));
            Assert.AreEqual(15, ActionModes.ResolveNextValue(values, 17, "toggle"));
            // Unknown current value falls back to the first.
            Assert.AreEqual(15, ActionModes.ResolveNextValue(values, 99, "toggle"));
            // A single value has nothing to toggle to.
            Assert.AreEqual(15, ActionModes.ResolveNextValue(new List<int> { 15 }, 15, "toggle"));
        }

        [Test]
        public void ResolveNextValue_Loop_Cycles_Through_Values()
        {
            var values = new List<int> { 15, 17, 18 };

            Assert.AreEqual(17, ActionModes.ResolveNextValue(values, 15, "loop"));
            Assert.AreEqual(18, ActionModes.ResolveNextValue(values, 17, "loop"));
            // Wraps from the last back to the first.
            Assert.AreEqual(15, ActionModes.ResolveNextValue(values, 18, "loop"));
            // Unknown current value falls back to the first.
            Assert.AreEqual(15, ActionModes.ResolveNextValue(values, 99, "loop"));
        }

        [Test]
        public void ResolveScrollValue_ScrollUp_Increases_By_Step()
        {
            Assert.AreEqual(55, ActionModes.ResolveScrollValue(50, 5, 0, 100, "scrollUp", false));
            Assert.AreEqual(100, ActionModes.ResolveScrollValue(98, 5, 0, 100, "scrollUp", false));
            // Wrap around.
            Assert.AreEqual(0, ActionModes.ResolveScrollValue(98, 5, 0, 100, "scrollUp", true));
        }

        [Test]
        public void ResolveScrollValue_ScrollDown_Decreases_By_Step()
        {
            Assert.AreEqual(45, ActionModes.ResolveScrollValue(50, 5, 0, 100, "scrollDown", false));
            Assert.AreEqual(0, ActionModes.ResolveScrollValue(2, 5, 0, 100, "scrollDown", false));
            // Wrap around.
            Assert.AreEqual(100, ActionModes.ResolveScrollValue(2, 5, 0, 100, "scrollDown", true));
        }

        [Test]
        public void InspectorDataSourceDTO_Serializes_With_Event_And_Items()
        {
            var dto = new InspectorDataSourceDTO("getMonitors", new List<IInspectorDataSourceItemDTO>
            {
                new InspectorDataSourceItemDTO("MODEL-A", "MODEL-A"),
                new InspectorDataSourceItemGroupDTO("MODEL-B", new List<IInspectorDataSourceItemDTO>
                {
                    new InspectorDataSourceItemDTO("Brightness", "Brightness")
                })
            });

            var json = JObject.FromObject(dto);

            Assert.AreEqual("getMonitors", json.Value<string>("event"));
            var items = (JArray)json["items"];
            Assert.AreEqual(2, items.Count);
            Assert.AreEqual("MODEL-A", items[0].Value<string>("value"));
            Assert.AreEqual("MODEL-B", items[1].Value<string>("label"));
            Assert.AreEqual("Brightness", items[1]["children"][0].Value<string>("value"));
        }

        [Test]
        public void IconManager_PowerStatus_Colors_Are_Complete()
        {
            foreach (MonitorStatus.PowerStatus status in System.Enum.GetValues(typeof(MonitorStatus.PowerStatus)))
            {
                Assert.IsTrue(IconManager.POWERSTATUS_COLORS.ContainsKey(status),
                    "missing icon color for power status " + status);
            }

            Assert.AreEqual(Color.LimeGreen, IconManager.POWERSTATUS_COLORS[MonitorStatus.PowerStatus.ON]);
            Assert.AreEqual(Color.Red, IconManager.POWERSTATUS_COLORS[MonitorStatus.PowerStatus.OFF]);
        }
    }
}
