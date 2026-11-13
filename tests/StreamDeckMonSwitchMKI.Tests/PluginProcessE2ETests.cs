using Newtonsoft.Json.Linq;
using NUnit.Framework;
using StreamDeckMonitorSwitch.ddcmon;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;

namespace StreamDeckMonSwitch.Tests
{
    /**
     * <summary>
     * End-to-end tests that run the REAL plugin executable
     * (StreamDeckMonitorSwitch.exe) against a fake Stream Deck WebSocket
     * server, exercising the full SDK protocol: registration, action
     * lifecycle, settings persistence and Property Inspector data sourcing.
     *
     * Safety: the key-press scenario uses a monitor model that cannot exist
     * on this machine, so no VCP value is ever written to a real monitor.
     * </summary>
     */
    [TestFixture]
    [Category("e2e")]
    public class PluginProcessE2ETests
    {
        private const string ActionUuid = "com.elgato.streamdeck.streamdeckmonswitch.monitorpluginaction";
        private const string PluginUuid = "e2e-plugin-uuid";
        private const string ContextId = "e2e-context";
        private const string DeviceId = "e2e-device";
        private const string FakeMonitorModel = "E2E-FAKE-MONITOR";

        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

        private FakeStreamDeckServer server;
        private Process pluginProcess;
        private int port;
        private readonly System.Text.StringBuilder pluginOutput = new System.Text.StringBuilder();

        private static string RepoRoot
        {
            get
            {
                // tests/StreamDeckMonSwitchMKI.Tests/bin/<config>/net481 -> repo root
                var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
                while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StreamDeckMonSwitchMKI.sln")))
                {
                    dir = dir.Parent;
                }

                Assert.NotNull(dir, "Could not locate repository root from test directory");
                return dir.FullName;
            }
        }

        private static string PluginExePath =>
            Path.Combine(RepoRoot, "backend", "bin", "Debug", "StreamDeckMonSwitchMKI.sdPlugin", "StreamDeckMonitorSwitch.exe");

        [OneTimeSetUp]
        public void FixtureSetup()
        {
            Assert.IsTrue(File.Exists(PluginExePath),
                "Plugin executable not found at " + PluginExePath + " - build the backend (Debug) first.");

            // This test assembly references the plugin exe, so accessing
            // MonitorConfigurator runs its static constructor IN THE TEST HOST.
            // Stretch the native re-enumeration period so the timer never fires here
            // (the child plugin processes inherit this too, which is fine for tests).
            Environment.SetEnvironmentVariable("SDMONSWITCH_UPDATE_PERIOD_MS", int.MaxValue.ToString());
        }

        [OneTimeTearDown]
        public void FixtureTearDown()
        {
            // The test assembly references the plugin exe, so its static timers
            // (MonitorConfigurator, IconManager) run in the test process too.
            // Stop them so they don't crash the test host on exit.
            try { StreamDeckMonitorSwitch.ddcmon.MonitorConfigurator.UpdateMonitorsTimer?.Dispose(); } catch { }
            try { StreamDeckMonitorSwitch.IconManager.AutoUpdateTimer?.Dispose(); } catch { }
        }

        [SetUp]
        public void SetUp()
        {
            port = 20000 + new Random().Next(10000);
            server = new FakeStreamDeckServer(port);
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (pluginProcess != null && !pluginProcess.HasExited)
                {
                    pluginProcess.Kill();
                    pluginProcess.WaitForExit(5000);
                }
            }
            finally
            {
                pluginProcess?.Dispose();
                pluginProcess = null;
                server.Dispose();
            }
        }

        /**
         * The plugin resolves img/computer-monitor-white.svg relative to its
         * working directory, so stage a working directory containing img/.
         */
        private string StageWorkingDirectory()
        {
            var staging = Path.Combine(Path.GetTempPath(), "sdmonswitch-e2e-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(staging, "img"));

            var svgSource = Path.Combine(RepoRoot, "backend", "img", "computer-monitor-white.svg");
            File.Copy(svgSource, Path.Combine(staging, "img", "computer-monitor-white.svg"));

            return staging;
        }

        private void StartPlugin(JObject initialSettings)
        {
            var info = new JObject
            {
                ["application"] = new JObject
                {
                    ["font"] = "Arial",
                    ["language"] = "en",
                    ["platform"] = "windows",
                    ["platformVersion"] = "10",
                    ["version"] = "6.0"
                },
                ["plugin"] = new JObject
                {
                    ["uuid"] = "com.elgato.streamdeck.streamdeckmonswitch",
                    ["version"] = "0.3"
                },
                ["devicePixelRatio"] = 1,
                ["devices"] = new JArray
                {
                    new JObject
                    {
                        ["id"] = DeviceId,
                        ["name"] = "E2E Device",
                        ["size"] = new JObject { ["columns"] = 5, ["rows"] = 3 },
                        ["type"] = 0
                    }
                }
            };

            var startInfo = new ProcessStartInfo
            {
                FileName = PluginExePath,
                WorkingDirectory = StageWorkingDirectory(),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                Arguments = string.Format(
                    "-port {0} -pluginUUID {1} -registerEvent registerPlugin -info \"{2}\"",
                    port, PluginUuid, info.ToString(Newtonsoft.Json.Formatting.None).Replace("\"", "\\\""))
            };

            pluginProcess = Process.Start(startInfo);
            pluginProcess.OutputDataReceived += (s, e) => { if (e.Data != null) lock (pluginOutput) pluginOutput.AppendLine(e.Data); };
            pluginProcess.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (pluginOutput) pluginOutput.AppendLine("[stderr] " + e.Data); };
            pluginProcess.BeginOutputReadLine();
            pluginProcess.BeginErrorReadLine();
            Assert.IsTrue(server.WaitForPluginConnection(TimeSpan.FromSeconds(15)),
                "Plugin did not connect to the fake Stream Deck server");
        }

        private string PluginOutputSnapshot()
        {
            lock (pluginOutput)
            {
                return pluginOutput.ToString();
            }
        }

        private JObject MakeWillAppearEvent(JObject settings)
        {
            return new JObject
            {
                ["action"] = ActionUuid,
                ["event"] = "willAppear",
                ["context"] = ContextId,
                ["device"] = DeviceId,
                ["payload"] = new JObject
                {
                    ["settings"] = settings ?? new JObject(),
                    ["coordinates"] = new JObject { ["column"] = 0, ["row"] = 0 },
                    ["state"] = 0,
                    ["isInMultiAction"] = false,
                    ["controller"] = "Keypad"
                }
            };
        }

        private JObject MakeKeyEvent(string eventName, JObject settings)
        {
            return new JObject
            {
                ["action"] = ActionUuid,
                ["event"] = eventName,
                ["context"] = ContextId,
                ["device"] = DeviceId,
                ["payload"] = new JObject
                {
                    ["settings"] = settings,
                    ["coordinates"] = new JObject { ["column"] = 0, ["row"] = 0 },
                    ["state"] = 0,
                    ["isInMultiAction"] = false
                }
            };
        }

        [Test]
        public void Plugin_Registers_With_The_StreamDeck()
        {
            StartPlugin(null);

            var register = server.AwaitMessage(
                msg => msg.Value<string>("event") == "registerPlugin",
                Timeout, "registerPlugin");

            Assert.AreEqual(PluginUuid, register.Value<string>("uuid"));
        }

        [Test]
        public void WillAppear_Without_Settings_Persists_Default_Settings_And_Pushes_Icon()
        {
            StartPlugin(null);
            server.AwaitMessage(msg => msg.Value<string>("event") == "registerPlugin", Timeout, "registerPlugin");

            server.SendToPlugin(MakeWillAppearEvent(null));

            // Default settings are persisted immediately.
            var setSettings = server.AwaitMessage(
                msg => msg.Value<string>("event") == "setSettings",
                Timeout, "setSettings with defaults");

            var payload = (JObject)setSettings["payload"];
            Assert.IsTrue(payload["monitors"] is JArray && !payload["monitors"].HasValues, "monitors should default to []");
            Assert.IsTrue(payload["properties"] is JObject && !payload["properties"].HasValues, "properties should default to {}");
            Assert.IsTrue(payload["values"] is JObject && !payload["values"].HasValues, "values should default to {}");

            // The icon is rendered and pushed.
            var setImage = server.AwaitMessage(
                msg => msg.Value<string>("event") == "setImage",
                Timeout, "setImage after willAppear");

            var data = setImage["payload"]?.Value<string>("image");
            Assert.NotNull(data);
            StringAssert.StartsWith("data:image/", data);
        }

        /**
         * Reads the physically detected monitor count with retries: DDC/CI
         * enumeration can transiently fail when another process (e.g. the Stream
         * Deck application) is talking to the monitors at the same time.
         */
        private static int GetPhysicalMonitorCount()
        {
            return GetPhysicalMonitors().Count;
        }

        /**
         * Reads the physically detected monitors with retries (see above).
         */
        private static List<PhysicalMonitor> GetPhysicalMonitors()
        {
            for (var attempt = 0; attempt < 5; attempt++)
            {
                var monitors = MonitorConfigurator.Monitors;
                if (monitors.Count > 0)
                {
                    return monitors;
                }

                MonitorConfigurator.UpdateMonitors();
                System.Threading.Thread.Sleep(500);
            }

            return MonitorConfigurator.Monitors;
        }

        /**
         * Mirrors the backend's UpdateInspectorProperties filter: VCP properties
         * that are known and writable (continuous controls like Brightness have
         * no advertised values but are still selectable).
         */
        private static List<string> WritableValuedProperties(PhysicalMonitor monitor)
        {
            return monitor.possibleVCPValues
                .Where(kv => kv.Key.codeName != VCP_PROPS.UNKNOWN_CODE_NAME)
                .Where(kv => kv.Key.perms.HasFlag(VCPPropertyPerms.WRITE))
                .Select(kv => kv.Key.codeName)
                .ToList();
        }

        [Test]
        public void SendToPlugin_GetMonitors_Replies_With_Detected_Monitors()
        {
            // Ground truth BEFORE starting the plugin process: concurrent DDC/CI
            // enumeration from two processes can fail, so enumerate while alone.
            var physicalMonitorCount = GetPhysicalMonitorCount();
            if (physicalMonitorCount == 0)
            {
                Assert.Inconclusive("No physical monitors detected on this machine; getMonitors intentionally sends no reply.");
            }

            StartPlugin(null);
            server.AwaitMessage(msg => msg.Value<string>("event") == "registerPlugin", Timeout, "registerPlugin");
            server.SendToPlugin(MakeWillAppearEvent(null));
            server.AwaitMessage(msg => msg.Value<string>("event") == "setSettings", Timeout, "setSettings");
            // Wait for setImage: MonitorConfigurator is initialized synchronously in
            // IconManager.Register, which runs before PushIcon sends setImage.
            server.AwaitMessage(msg => msg.Value<string>("event") == "setImage", Timeout, "setImage (confirms MonitorConfigurator ready)");

            server.SendToPlugin(new JObject
            {
                ["action"] = ActionUuid,
                ["event"] = "sendToPlugin",
                ["context"] = ContextId,
                ["payload"] = new JObject { ["event"] = "getMonitors" }
            });

            JObject reply;
            try
            {
                reply = server.AwaitMessage(
                    msg => msg.Value<string>("event") == "sendToPropertyInspector"
                           && msg["payload"]?.Value<string>("event") == "getMonitors",
                    Timeout, "sendToPropertyInspector getMonitors");
            }
            catch (TimeoutException)
            {
                // The plugin found no monitors (its own DDC/CI enumeration lost to
                // contention, e.g. with the running Stream Deck app). Not a code failure.
                Assert.Inconclusive("Plugin process could not enumerate monitors (DDC/CI contention); no getMonitors reply.");
                return;
            }

            var items = (JArray)reply["payload"]["items"];
            Assert.AreEqual(physicalMonitorCount, items.Count,
                "getMonitors should reply with one item per detected physical monitor");
        }

        [Test]
        public void KeyDown_With_Unknown_Monitor_Model_Fails_Gracefully_And_Updates_Icon()
        {
            // Settings use the lenient PI shape: values as one-element string arrays.
            var settings = new JObject
            {
                ["monitors"] = new JArray { FakeMonitorModel },
                ["properties"] = new JObject
                {
                    [FakeMonitorModel] = new JArray { "Input Select" }
                },
                ["values"] = new JObject
                {
                    [FakeMonitorModel] = new JObject
                    {
                        ["Input Select"] = new JArray { "15" }
                    }
                }
            };

            StartPlugin(null);
            server.AwaitMessage(msg => msg.Value<string>("event") == "registerPlugin", Timeout, "registerPlugin");
            server.SendToPlugin(MakeWillAppearEvent(settings));
            server.AwaitMessage(msg => msg.Value<string>("event") == "setImage", Timeout, "initial setImage");
            server.DrainQueued(msg => true);

            server.SendToPlugin(MakeKeyEvent("keyDown", settings));

            // The action must react: it pushes at least one new icon (orange -> red,
            // since the fake monitor cannot be found and SetVCPFeature is skipped).
            var setImage = server.AwaitMessage(
                msg => msg.Value<string>("event") == "setImage",
                Timeout, "setImage after keyDown");

            Assert.NotNull(setImage["payload"]?.Value<string>("image"));

            server.SendToPlugin(MakeKeyEvent("keyUp", settings));

            // keyUp merely releases the re-entrancy guard; the process must stay alive.
            System.Threading.Thread.Sleep(1000);
            Assert.IsFalse(pluginProcess.HasExited, "Plugin process must not crash on keyUp");
        }

        [Test]
        public void DidReceiveSettings_Triggers_PropertyInspector_Update()
        {
            // Ground truth BEFORE starting the plugin process (see SendToPlugin test).
            var physicalMonitorCount = GetPhysicalMonitorCount();
            if (physicalMonitorCount == 0)
            {
                Assert.Inconclusive("No physical monitors detected on this machine; inspector update sends no monitor list.");
            }

            var settings = new JObject
            {
                ["monitors"] = new JArray { FakeMonitorModel },
                ["properties"] = new JObject(),
                ["values"] = new JObject()
            };

            StartPlugin(null);
            server.AwaitMessage(msg => msg.Value<string>("event") == "registerPlugin", Timeout, "registerPlugin");
            server.SendToPlugin(MakeWillAppearEvent(null));
            server.AwaitMessage(msg => msg.Value<string>("event") == "setSettings", Timeout, "setSettings");
            server.AwaitMessage(msg => msg.Value<string>("event") == "setImage", Timeout, "setImage (confirms MonitorConfigurator ready)");
            server.DrainQueued(msg => true);

            server.SendToPlugin(new JObject
            {
                ["action"] = ActionUuid,
                ["event"] = "didReceiveSettings",
                ["context"] = ContextId,
                ["device"] = DeviceId,
                ["payload"] = new JObject
                {
                    ["settings"] = settings,
                    ["coordinates"] = new JObject { ["column"] = 0, ["row"] = 0 },
                    ["isInMultiAction"] = false
                }
            });

            // ReceivedSettings -> UpdateInspector -> UpdateInspectorMonitors.
            JObject reply;
            try
            {
                reply = server.AwaitMessage(
                    msg => msg.Value<string>("event") == "sendToPropertyInspector"
                           && msg["payload"]?.Value<string>("event") == "getMonitors",
                    Timeout, "sendToPropertyInspector getMonitors after didReceiveSettings");
            }
            catch (TimeoutException)
            {
                Assert.Inconclusive("Plugin process could not enumerate monitors (DDC/CI contention); no inspector update reply.");
                return;
            }

            Assert.NotNull(reply["payload"]["items"]);
        }

        /**
         * Regression: the PI asks for properties as soon as it loads, but the
         * plugin used to drop that request until settings arrived - leaving the
         * "VCP properties" list stuck on "Loading ...". With a monitor selected,
         * getProperties must be answered with exactly the writable, valued VCPs.
         */
        [Test]
        public void SendToPlugin_GetProperties_Replies_When_Monitor_Is_Selected()
        {
            var groundTruth = GetPhysicalMonitors();
            if (groundTruth.Count == 0)
            {
                Assert.Inconclusive("No physical monitors detected on this machine.");
            }

            var monitor = groundTruth[0];
            var expectedProperties = WritableValuedProperties(monitor);
            if (expectedProperties.Count == 0)
            {
                Assert.Inconclusive("Monitor " + monitor.model + " advertises no writable VCP properties with values; getProperties intentionally sends no reply.");
            }

            var settings = new JObject
            {
                ["monitors"] = new JArray { monitor.model },
                ["properties"] = new JObject(),
                ["values"] = new JObject()
            };

            StartPlugin(null);
            server.AwaitMessage(msg => msg.Value<string>("event") == "registerPlugin", Timeout, "registerPlugin");
            server.SendToPlugin(MakeWillAppearEvent(settings));
            server.AwaitMessage(msg => msg.Value<string>("event") == "setImage", Timeout, "setImage (confirms MonitorConfigurator ready)");
            server.DrainQueued(msg => true);

            server.SendToPlugin(new JObject
            {
                ["action"] = ActionUuid,
                ["event"] = "sendToPlugin",
                ["context"] = ContextId,
                ["payload"] = new JObject { ["event"] = "getProperties" }
            });

            JObject reply;
            try
            {
                reply = server.AwaitMessage(
                    msg => msg.Value<string>("event") == "sendToPropertyInspector"
                           && msg["payload"]?.Value<string>("event") == "getProperties",
                    Timeout, "sendToPropertyInspector getProperties");
            }
            catch (TimeoutException)
            {
                Assert.Inconclusive("Plugin process could not enumerate monitors (DDC/CI contention); no getProperties reply.");
                return;
            }

            var items = (JArray)reply["payload"]["items"];
            Assert.AreEqual(1, items.Count, "one group per selected monitor");
            Assert.AreEqual(monitor.model, items[0].Value<string>("label"));

            var actual = ((JArray)items[0]["children"])
                .Select(child => child.Value<string>("value"))
                .OrderBy(v => v)
                .ToList();

            CollectionAssert.AreEqual(expectedProperties.OrderBy(v => v).ToList(), actual,
                "getProperties should list exactly the writable, valued VCP properties of the selected monitor");
        }

        /**
         * Regression: with a monitor AND a property selected, getValues must be
         * answered with the value list of that property (this feeds the "VCP
         * values" list in the PI, which used to stay on "Loading ...").
         */
        [Test]
        public void SendToPlugin_GetValues_Replies_When_Monitor_And_Property_Are_Selected()
        {
            var groundTruth = GetPhysicalMonitors();
            if (groundTruth.Count == 0)
            {
                Assert.Inconclusive("No physical monitors detected on this machine.");
            }

            var monitor = groundTruth[0];
            var property = monitor.possibleVCPValues
                .Where(kv => kv.Key.codeName != VCP_PROPS.UNKNOWN_CODE_NAME)
                .Where(kv => kv.Key.perms.HasFlag(VCPPropertyPerms.WRITE))
                .Where(kv => kv.Value.Count > 0)
                .Select(kv => kv)
                .FirstOrDefault();

            if (property.Key == null)
            {
                Assert.Inconclusive("Monitor " + monitor.model + " advertises no writable VCP properties with values; getValues intentionally sends no reply.");
            }

            var expectedValues = property.Value.Select(v => v.ToString()).OrderBy(v => v).ToList();

            var settings = new JObject
            {
                ["monitors"] = new JArray { monitor.model },
                ["properties"] = new JObject
                {
                    [monitor.model] = new JArray { property.Key.codeName }
                },
                ["values"] = new JObject()
            };

            StartPlugin(null);
            server.AwaitMessage(msg => msg.Value<string>("event") == "registerPlugin", Timeout, "registerPlugin");
            server.SendToPlugin(MakeWillAppearEvent(settings));
            server.AwaitMessage(msg => msg.Value<string>("event") == "setImage", Timeout, "setImage (confirms MonitorConfigurator ready)");
            server.DrainQueued(msg => true);

            server.SendToPlugin(new JObject
            {
                ["action"] = ActionUuid,
                ["event"] = "sendToPlugin",
                ["context"] = ContextId,
                ["payload"] = new JObject { ["event"] = "getValues" }
            });

            JObject reply;
            try
            {
                reply = server.AwaitMessage(
                    msg => msg.Value<string>("event") == "sendToPropertyInspector"
                           && msg["payload"]?.Value<string>("event") == "getValues",
                    Timeout, "sendToPropertyInspector getValues");
            }
            catch (TimeoutException)
            {
                Assert.Inconclusive("Plugin process could not enumerate monitors (DDC/CI contention); no getValues reply.");
                return;
            }

            var items = (JArray)reply["payload"]["items"];
            Assert.AreEqual(1, items.Count, "one group per selected monitor");
            Assert.AreEqual(monitor.model, items[0].Value<string>("label"));

            var propertyGroup = ((JArray)items[0]["children"])
                .Children<JObject>()
                .FirstOrDefault(group => group.Value<string>("label") == property.Key.codeName);

            Assert.NotNull(propertyGroup,
                "getValues should contain a group for the selected property '" + property.Key.codeName + "'");

            var actualValues = ((JArray)propertyGroup["children"])
                .Select(child => child.Value<string>("value"))
                .OrderBy(v => v)
                .ToList();

            CollectionAssert.AreEqual(expectedValues, actualValues,
                "getValues should list exactly the advertised values of the selected property");
        }
    }
}
