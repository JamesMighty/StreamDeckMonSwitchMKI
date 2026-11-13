using BarRaider.SdTools;
using StreamDeckMonitorSwitch.ddcmon;
using Svg;
using Svg.Transforms;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;

namespace StreamDeckMonitorSwitch
{
    public static class IconManager
    {
        // The icon refresh reads VCP values over DDC/CI, which contends with the
        // PI data sourcing and key presses on the same bus. Keep it infrequent.
        public static readonly int TIMER_DUE_TIME = 1000 * 15;
        public static readonly int TIMER_PERIOD = 1000 * 10;
        public static readonly Dictionary<MonitorStatus.PowerStatus, Color> POWERSTATUS_COLORS = new Dictionary<MonitorStatus.PowerStatus, Color>()
        {
            { MonitorStatus.PowerStatus.OFF, Color.Red },
            { MonitorStatus.PowerStatus.ON, Color.LimeGreen },
            { MonitorStatus.PowerStatus.UNKNOWN, Color.Gray },
            { MonitorStatus.PowerStatus.SLEEP, Color.Yellow }
        };

        public static Timer AutoUpdateTimer { get; private set; }

        private static readonly object autoUpdateTimerLock = new object();

        private static List<MonitorPluginAction> RegisteredActions { get; } = new List<MonitorPluginAction>();

        /**
         * The auto-update timer is created lazily on first registration instead of in
         * the static constructor, so merely loading this assembly (e.g. from a test
         * host) does not start native DDC/CI polling.
         */
        private static void EnsureAutoUpdateTimer()
        {
            if (AutoUpdateTimer != null)
            {
                return;
            }

            lock (autoUpdateTimerLock)
            {
                if (AutoUpdateTimer == null)
                {
                    AutoUpdateTimer = new Timer(
                        new TimerCallback((obj) => IconTimerCallback((TimerState)obj)),
                        new TimerState(),
                        TIMER_DUE_TIME,
                        TIMER_PERIOD
                        );
                }
            }
        }

        private static void IconTimerCallback(TimerState state)
        {
            if (state.CurrentStatus == TimerStates.IDLE)
            {
                state.CurrentStatus = TimerStates.WORKING;
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
                finally
                {
                    state.CurrentStatus = TimerStates.IDLE;
                }
            }
        }

        public static SvgDocument MakeIcon(MonitorPluginAction action)
        {
            if (action == null)
            {
                throw new ArgumentNullException("Argument 'action' cannot be null.");
            }

            SvgDocument icon = SvgDocument.Open("img/computer-monitor-white.svg");

            // Vertical squish applied to main_layer for single-monitor icons; the
            // VCP glyph compensates for it so it stays undistorted.
            float glyphYScale = 1f;

            if (action.Settings.Properties.Count > 1)
            {
                Logger.Instance.LogMessage(TracingLevel.DEBUG, "Making multi-monitor icon");

                var background_mon = (SvgElement)icon.GetElementById("main_layer").Clone();
                background_mon.ID = "backgroound_layer";

                foreach ( var child in background_mon.Children)
                {
                    child.ID = "background_"+child.ID;
                }
                background_mon.Transforms.Add(new SvgTranslate(5, -5));

                Logger.Instance.LogMessage(
                    TracingLevel.DEBUG,
                    background_mon.ToString()
                    );

                background_mon.Opacity = 0.5f;
                icon.Children.Insert(1, background_mon);

            }else if (action.Settings.Properties.Count == 1)
            {
                Logger.Instance.LogMessage(TracingLevel.DEBUG, "Making single-monitor icon");

                var physicalMonitors = MonitorConfigurator.Monitors.Where(
                    mon => mon.model == action.Settings.Properties.First().Key
                );

                if (physicalMonitors.Count() > 0) {
                    var physicalMonitor = physicalMonitors.First();

                    int monitor_width = physicalMonitor.info.monitor.right - physicalMonitor.info.monitor.left;
                    int monitor_height = physicalMonitor.info.monitor.bottom - physicalMonitor.info.monitor.top;
                    var yScaleRaw = (monitor_height / (float)monitor_width);
                    var yScale = 1.5f * yScaleRaw;
                    glyphYScale = yScale;

                    icon.GetElementById("main_layer").Transforms.Add(new SvgScale(1, yScale));

                    icon.GetElementById("monitor_button").Transforms = new SvgTransformCollection() {
                        new SvgTranslate(0, -( 12f / yScale ) + 12),
                        new SvgScale(1, 1/yScale)
                    };
                }
            }

            // Iconography: merge VCP-type indicators into the monitor geometry.
            ApplyVcpIconography(action, icon, glyphYScale);

            return icon;
        }

        /**
         * Merges VCP-type iconography into the monitor geometry:
         * - Power mode: IEC 5009 symbol REPLACES the power-button dot (keeps
         *   id "monitor_button" so power-state recoloring works unchanged).
         * - Input select: arrow entering the screen from the right edge.
         * - Others: glyph centered in the screen area.
         */
        private static void ApplyVcpIconography(MonitorPluginAction action, SvgDocument icon, float glyphYScale)
        {
            var propertyNames = action.Settings.Properties
                .SelectMany(kv => kv.Value)
                .Distinct()
                .ToList();

            if (propertyNames.Count == 0)
                return;

            var name = propertyNames.Count == 1 ? propertyNames[0] : null;
            var mainLayer = icon.GetElementById("main_layer");

            if (name == VCP_PROPS.POWER_MODE.codeName)
            {
                // Replace the power-button dot with the IEC 5009 power symbol.
                // The group keeps id "monitor_button" so ApplyIconColors recolors it.
                var oldButton = icon.GetElementById("monitor_button");
                var parent = oldButton.Parent;

                // Drawn at the old dot's native center (30.4, 7.04) so the old
                // dot's transforms (squish compensation) can be reused verbatim.
                var powerGroup = ParseSvgGroup(
                    "<g id='monitor_button' fill='none' stroke='#ffffff' stroke-width='2' stroke-linecap='round'>"
                    + "<path d='M 30.4 2.5 L 30.4 7'/>"
                    + "<path d='M 27.6 4.2 A 4 4 0 1 0 33.2 4.2'/>"
                    + "</g>");

                if (powerGroup != null)
                {
                    if (oldButton.Transforms != null && oldButton.Transforms.Count > 0)
                    {
                        powerGroup.Transforms = oldButton.Transforms;
                    }

                    parent.Children.Remove(oldButton);
                    parent.Children.Add(powerGroup);
                }
            }
            else if (name == VCP_PROPS.BRIGHTNESS.codeName)
            {
                // Replace the power-button dot with a full-size brightness sun in
                // the screen center. The sun keeps id "monitor_button" so it still
                // receives power-state recoloring.
                var oldButton = icon.GetElementById("monitor_button");
                var parent = oldButton.Parent;

                var displayValue = ResolveDisplayValue(action, name, null);
                var sunGroup = MakeBrightnessSunGroup(displayValue ?? 50);

                if (sunGroup != null)
                {
                    sunGroup.Transforms = new SvgTransformCollection
                    {
                        new SvgTranslate(30.4f, -5f),
                        new SvgScale(1f, 1f / glyphYScale)
                    };

                    parent.Children.Remove(oldButton);
                    mainLayer.Children.Add(sunGroup);
                }
            }
            else if (name == VCP_PROPS.INPUT_SELECT.codeName)
            {
                // Arrow entering the screen from the right edge (source switch).
                var glyph = ParseSvgGroup(
                    "<g id='vcp_glyph' stroke='#ffffff' stroke-width='2.5' fill='none' stroke-linecap='round' stroke-linejoin='round'>"
                    + "<path d='M 10 0 L -2 0 M 2 -4 L -2 0 L 2 4'/>"
                    + "</g>");
                if (glyph != null)
                {
                    glyph.Transforms = new SvgTransformCollection
                    {
                        new SvgTranslate(44f, -5f),
                        new SvgScale(1f, 1f / glyphYScale)
                    };
                    mainLayer.Children.Add(glyph);
                }
            }
            else
            {
                // All other types: glyph centered in the screen area. For set-mode
                // continuous controls we can show the target value immediately;
                // scroll modes have no live reading yet so they default to mid.
                var displayValue = ResolveDisplayValue(action, name, null);
                var initialValues = displayValue.HasValue
                    ? new Dictionary<string, int> { { name, displayValue.Value } }
                    : null;
                var glyph = MakeScreenGlyph(name, initialValues);
                if (glyph != null)
                {
                    glyph.Transforms = new SvgTransformCollection
                    {
                        new SvgTranslate(30.4f, -5f),
                        new SvgScale(1f, 1f / glyphYScale)
                    };
                    mainLayer.Children.Add(glyph);
                }
            }
        }

        /**
         * Builds a screen-centered glyph for VCP types that don't have a
         * dedicated monitor-geometry merge point.
         */
        private static SvgElement MakeScreenGlyph(string name, Dictionary<string, int> currentValues)
        {
            // Live value for continuous controls; defaults to a mid value until
            // the timer or a key-press refresh supplies the real reading.
            int value = 50;
            if (currentValues != null && currentValues.TryGetValue(name, out var liveValue))
            {
                value = liveValue;
            }

            string body;
            if (name == VCP_PROPS.BRIGHTNESS.codeName)
            {
                // Sun whose rays grow with the current brightness value.
                body = MakeBrightnessGlyphBody(value);
            }
            else if (name == VCP_PROPS.CONTRAST.codeName)
            {
                // Circle that fills proportionally to the current contrast value.
                body = MakeContrastGlyphBody(value);
            }
            else if (name == VCP_PROPS.SELECT_COLOR_PRESET.codeName)
            {
                // Three overlapping RGB dots.
                body = "<circle cx='-3.2' cy='2.6' r='3.4' fill='#e05050' stroke='none'/><circle cx='3.2' cy='2.6' r='3.4' fill='#50b050' stroke='none'/><circle cx='0' cy='-3' r='3.4' fill='#5080e0' stroke='none'/>";
            }
            else if (name == VCP_PROPS.OSD_LANGUAGE.codeName)
            {
                // Speech bubble.
                body = "<path d='M -8 -6.5 H 8 V 3.5 H -1.5 L -5.5 7.5 V 3.5 H -8 Z'/>";
            }
            else if (name == VCP_PROPS.OSD.codeName)
            {
                // Menu lines.
                body = "<path d='M -6.5 -4.5 H 6.5 M -6.5 0 H 6.5 M -6.5 4.5 H 6.5'/>";
            }
            else if (name == VCP_PROPS.VIDEO_GAIN_DRIVE_RED.codeName
                  || name == VCP_PROPS.VIDEO_GAIN_DRIVE_GREEN.codeName
                  || name == VCP_PROPS.VIDEO_GAIN_DRIVE_BLUE.codeName)
            {
                // Slider with a channel-coloured knob.
                var knobColor = name == VCP_PROPS.VIDEO_GAIN_DRIVE_RED.codeName ? "#e05050"
                    : name == VCP_PROPS.VIDEO_GAIN_DRIVE_GREEN.codeName ? "#50b050"
                    : "#5080e0";
                body = "<path d='M 0 -7.5 V 7.5'/><circle cx='0' cy='-2' r='3.2' fill='" + knobColor + "'/>";
            }
            else if (name == VCP_PROPS.NEW_CONTROL_VALUE.codeName
                  || name == VCP_PROPS.RESTORE_FACTORY_DEFAULTS.codeName
                  || name == VCP_PROPS.RESTORE_FACTORY_LUMINANCE_CONTRAST.codeName
                  || name == VCP_PROPS.RESTORE_FACTORY_COLOR_DEFAULTS.codeName)
            {
                // Circular refresh arrow.
                body = "<path d='M 6.5 -3 A 7 7 0 1 0 5 4.5'/><path d='M 6.5 -8 V -3 H 1.5'/>";
            }
            else
            {
                // Generic: three sliders with knobs.
                body = "<path d='M -7 -4.5 H 7 M -7 0 H 7 M -7 4.5 H 7'/><circle cx='2.5' cy='-4.5' r='2.2' fill='#ffffff' stroke='none'/><circle cx='-3' cy='0' r='2.2' fill='#ffffff' stroke='none'/><circle cx='3.5' cy='4.5' r='2.2' fill='#ffffff' stroke='none'/>";
            }

            return ParseSvgGroup(
                "<g id='vcp_glyph' stroke='#ffffff' stroke-width='2' fill='none' stroke-linecap='round' stroke-linejoin='round'>"
                + body
                + "</g>");
        }

        /**
         * Brightness glyph with a moon-to-sun progression so low values stay
         * readable: 0 rays renders a hollow crescent moon, 1 ray a filled
         * crescent, and 2+ rays switch to a sun whose rays grow with the value.
         * (The wrapping group uses fill='none', so fills must be explicit.)
         */
        private static string MakeBrightnessGlyphBody(int value)
        {
            var clamped = Math.Max(0, Math.Min(100, value));
            int numRays = (int)Math.Round(clamped / 100.0 * 8);

            if (numRays == 0)
            {
                // Hollow crescent outline (inherits the group's fill='none').
                return "<path d='" + MakeCrescentPath(3.6, 2.5) + "' stroke-width='1.5'/>";
            }
            if (numRays == 1)
            {
                // Filled crescent.
                return "<path d='" + MakeCrescentPath(3.6, 2.5) + "' fill='#ffffff' stroke='none'/>";
            }

            var body = "<circle cx='0' cy='0' r='3.2' fill='#ffffff' stroke='none'/>";
            double innerR = 5.5;
            double outerR = 8.0;
            for (int i = 0; i < numRays; i++)
            {
                double rad = i * 45.0 * Math.PI / 180.0;
                double x1 = Math.Round(innerR * Math.Sin(rad), 1);
                double y1 = Math.Round(-innerR * Math.Cos(rad), 1);
                double x2 = Math.Round(outerR * Math.Sin(rad), 1);
                double y2 = Math.Round(-outerR * Math.Cos(rad), 1);
                body += "<path d='M " + x1.ToString("0.#") + " " + y1.ToString("0.#") + " L " + x2.ToString("0.#") + " " + y2.ToString("0.#") + "'/>";
            }
            return body;
        }

        /**
         * Full-size brightness glyph drawn centered at the origin, using a
         * moon-to-sun progression so low values stay readable: 0 rays renders a
         * hollow crescent moon, 1 ray a filled crescent, and 2+ rays switch to a
         * sun. The caller positions it via transforms (screen center). Keeps id
         * "monitor_button" so it replaces the power-button dot and receives
         * power-state recoloring. The center disc inherits the group fill; the
         * rays inherit the stroke.
         */
        private static SvgElement MakeBrightnessSunGroup(int value)
        {
            var clamped = Math.Max(0, Math.Min(100, value));
            int numRays = (int)Math.Round(clamped / 100.0 * 8);

            double discR = 3.2;
            double innerR = 5.5;
            double outerR = 8.0;

            string body;
            var crescent = MakeCrescentPath(3.6, 2.5);
            if (numRays == 0)
            {
                // Hollow crescent: override the group fill so only the stroke shows.
                body = "<path d='" + crescent + "' fill='none' stroke-width='1.5'/>";
            }
            else if (numRays == 1)
            {
                // Filled crescent (inherits the group fill).
                body = "<path d='" + crescent + "' stroke='none'/>";
            }
            else
            {
                // Sun: filled disc plus rays.
                body = "<circle cx='0' cy='0' r='" + discR + "'/>";
                for (int i = 0; i < numRays; i++)
                {
                    double rad = i * 45.0 * Math.PI / 180.0;
                    double x1 = Math.Round(innerR * Math.Sin(rad), 2);
                    double y1 = Math.Round(-innerR * Math.Cos(rad), 2);
                    double x2 = Math.Round(outerR * Math.Sin(rad), 2);
                    double y2 = Math.Round(-outerR * Math.Cos(rad), 2);
                    body += "<path d='M " + x1.ToString("0.##") + " " + y1.ToString("0.##") + " L " + x2.ToString("0.##") + " " + y2.ToString("0.##") + "'/>";
                }
            }

            return ParseSvgGroup(
                "<g id='monitor_button' fill='#ffffff' stroke='#ffffff' stroke-width='2' stroke-linecap='round'>"
                + body
                + "</g>");
        }

        /**
         * Crescent moon path centered at the origin, opening to the right like
         * the classic moon icon. The outer edge is the left semicircle of a disc
         * with radius r; the terminator (inner edge) is an arc that bites into
         * the disc so the lit crescent is `thickness` wide at its widest point.
         */
        private static string MakeCrescentPath(double r, double thickness)
        {
            // Terminator arc circle: center (cx, 0), passing through (0, ±r) and
            // the inner tip (-(r - thickness), 0).
            double cx = thickness * (2 * r - thickness) / (2 * (r - thickness));
            double innerR = cx + (r - thickness);
            var rs = r.ToString("0.##");
            var irs = innerR.ToString("0.##");
            return "M 0 -" + rs
                + " A " + rs + " " + rs + " 0 0 0 0 " + rs
                + " A " + irs + " " + irs + " 0 0 1 0 -" + rs
                + " Z";
        }

        /**
         * Contrast glyph: an outline circle that fills as a pie slice proportional
         * to the current value.
         */
        private static string MakeContrastGlyphBody(int value)
        {
            var clamped = Math.Max(0, Math.Min(100, value));
            double r = 6.5;
            double fraction = clamped / 100.0;

            var body = "<circle cx='0' cy='0' r='" + r + "'/>";
            if (fraction <= 0.001)
                return body;
            if (fraction >= 0.999)
                return body + "<circle cx='0' cy='0' r='" + r + "' fill='#ffffff' stroke='none'/>";

            double angle = fraction * 360.0;
            double startRad = -90.0 * Math.PI / 180.0;
            double endRad = (-90.0 + angle) * Math.PI / 180.0;
            double x1 = Math.Round(r * Math.Cos(startRad), 2);
            double y1 = Math.Round(r * Math.Sin(startRad), 2);
            double x2 = Math.Round(r * Math.Cos(endRad), 2);
            double y2 = Math.Round(r * Math.Sin(endRad), 2);
            int largeArc = angle > 180 ? 1 : 0;
            body += "<path d='M 0 0 L " + x1 + " " + y1 + " A " + r + " " + r + " 0 " + largeArc + " 1 " + x2 + " " + y2 + " Z' fill='#ffffff' stroke='none'/>";
            return body;
        }

        /**
         * True for VCP properties that are continuous (value-range) controls and
         * therefore render a value-dependent glyph.
         */
        private static bool IsContinuousProperty(string name)
        {
            return name == VCP_PROPS.BRIGHTNESS.codeName || name == VCP_PROPS.CONTRAST.codeName;
        }

        /**
         * Reads the live value of every continuous property this action displays and
         * stores it on the matching monitor status.
         */
        private static void ReadContinuousValues(MonitorPluginAction action, Dictionary<string, MonitorStatus> monitorStates)
        {
            var continuousProps = action.Settings.Properties
                .SelectMany(kv => kv.Value)
                .Distinct()
                .Where(IsContinuousProperty)
                .ToList();

            if (continuousProps.Count == 0)
                return;

            foreach (var model in action.Settings.Properties.Keys)
            {
                if (!monitorStates.TryGetValue(model, out var status))
                    continue;

                var monitor = MonitorConfigurator.Monitors.FirstOrDefault(m => m.model == model);
                if (monitor == null)
                    continue;

                foreach (var prop in continuousProps)
                {
                    if (!VCP_PROPS.IndexByCodeName.TryGetValue(prop, out var vcpProp))
                        continue;

                    byte current = 0;
                    byte max = 0;
                    if (MonitorConfigurator.GetVCPFeatureAndVCPFeatureReply(monitor.Struct.hPhysicalMonitor, vcpProp.code, 0, ref current, ref max))
                    {
                        status.ContinuousValues[prop] = current;
                    }
                }
            }
        }

        /**
         * Regenerates the value-dependent screen glyph (brightness/contrast) in
         * place, preserving the transforms applied when the icon was built.
         */
        private static void RefreshValueGlyph(MonitorPluginAction action, Dictionary<string, MonitorStatus> monitorStates)
        {
            var propertyNames = action.Settings.Properties.SelectMany(kv => kv.Value).Distinct().ToList();
            var name = propertyNames.Count == 1 ? propertyNames[0] : null;
            if (name == null || !IsContinuousProperty(name))
                return;

            // Brightness replaces the power-button dot (id "monitor_button");
            // other continuous glyphs live in the screen area (id "vcp_glyph").
            var isBrightnessButton = name == VCP_PROPS.BRIGHTNESS.codeName;
            var glyphId = isBrightnessButton ? "monitor_button" : "vcp_glyph";

            var oldGlyph = action.svgIcon.GetElementById(glyphId);
            if (oldGlyph == null)
                return;

            var displayValue = ResolveDisplayValue(action, name, monitorStates);

            SvgElement newGlyph;
            if (isBrightnessButton)
            {
                newGlyph = MakeBrightnessSunGroup(displayValue ?? 50);
            }
            else
            {
                var currentValues = displayValue.HasValue
                    ? new Dictionary<string, int> { { name, displayValue.Value } }
                    : new Dictionary<string, int>();
                newGlyph = MakeScreenGlyph(name, currentValues);
            }
            if (newGlyph == null)
                return;

            newGlyph.Transforms = oldGlyph.Transforms;
            var parent = oldGlyph.Parent;
            parent.Children.Remove(oldGlyph);
            parent.Children.Add(newGlyph);
        }

        /**
         * Resolves the value to display on a continuous-property glyph.
         * In "set" mode the icon shows the configured target value (what the
         * button will apply); in scroll modes it shows the monitor's live reading.
         * Returns null when no value is available yet.
         */
        private static int? ResolveDisplayValue(MonitorPluginAction action, string propertyName, Dictionary<string, MonitorStatus> monitorStates)
        {
            if (action.Settings.ActionMode == ActionModes.SET)
            {
                return action.Settings.TargetValue;
            }

            // Scroll modes: use the live reading from the monitor.
            if (monitorStates != null)
            {
                foreach (var model in action.Settings.Properties.Keys)
                {
                    if (monitorStates.TryGetValue(model, out var status)
                        && status.ContinuousValues.TryGetValue(propertyName, out var val))
                    {
                        return val;
                    }
                }
            }
            return null;
        }

        /**
         * Parses an SVG group element from a markup string. Returns null on failure.
         */
        private static SvgElement ParseSvgGroup(string groupMarkup)
        {
            var svg = "<svg xmlns='http://www.w3.org/2000/svg'>" + groupMarkup + "</svg>";
            try
            {
                var doc = SvgDocument.FromSvg<SvgDocument>(svg);
                return doc.Children.OfType<SvgGroup>().FirstOrDefault();
            }
            catch (Exception err)
            {
                Logger.Instance.LogMessage(TracingLevel.WARN, "IconManager: Could not parse SVG group - " + err.ToString());
                return null;
            }
        }

        public static void Register(MonitorPluginAction action)
        {
            if (!RegisteredActions.Contains(action))
            {
                EnsureAutoUpdateTimer();
                action.svgIcon = MakeIcon(action);
                action.PushIcon();
                RegisteredActions.Add(action);
            }else
            {
                Logger.Instance.LogMessage(
                    TracingLevel.WARN,
                    string.Format("RegisteredActions already contains {0} action", action.ContextId)
                    );
            }
        }

        public static void Unregister(MonitorPluginAction action)
        {
            if (RegisteredActions.Contains(action))
            {
                RegisteredActions.Remove(action);
            }
        }

        /**
         * Reads current VCP state for a single action's configured monitors and
         * applies the correct outline/power colors immediately. Used after a key
         * press so the icon reflects the new state without waiting for the timer.
         */
        public static void RefreshActionIcon(MonitorPluginAction action)
        {
            if (action.Settings.Properties.Count == 0)
                return;

            var configuredModels = action.Settings.Properties.Keys.ToList();
            var monitorStates = new Dictionary<string, MonitorStatus>();

            foreach (PhysicalMonitor mon in MonitorConfigurator.Monitors.ToList())
            {
                if (!configuredModels.Contains(mon.model))
                    continue;

                MonitorStatus monitorStatus = new MonitorStatus();
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
                        monitorStatus.Power = isOn ? MonitorStatus.PowerStatus.ON : MonitorStatus.PowerStatus.OFF;
                    }
                    else
                    {
                        monitorStatus.Power = MonitorStatus.PowerStatus.UNKNOWN;
                    }
                }
                monitorStates.Add(mon.model, monitorStatus);
            }

            // Under DDC/CI contention a configured monitor can transiently be
            // missing from the detected set; keep the last icon instead of crashing.
            if (!configuredModels.All(model => monitorStates.ContainsKey(model)))
                return;

            ReadContinuousValues(action, monitorStates);
            RefreshValueGlyph(action, monitorStates);
            ApplyIconColors(action, monitorStates);
            action.PushIcon();
        }

        /**
         * Applies the outline and power-button colors based on current monitor state.
         * Shared between the periodic timer and the on-demand refresh.
         */
        private static void ApplyIconColors(MonitorPluginAction action, Dictionary<string, MonitorStatus> monitorStates)
        {
            var configuredModels = action.Settings.Properties.Keys.ToList();

            var finalPowerStatus = monitorStates[configuredModels.Aggregate((a, b) => monitorStates[a].Power > monitorStates[b].Power ? a : b)];
            var monitorsSelectedAsWanted = action.Settings.VCPActionsSettings.Where(vcpAction =>
                vcpAction.Value.Where(vcp => vcp.Key == VCP_PROPS.INPUT_SELECT.codeName && vcp.Value.Contains((int)monitorStates[vcpAction.Key].SelectedInput)).Count() > 0
            ).Count();
            var monitorsWhereSelectionWanted = action.Settings.VCPActionsSettings.Where(vcpAction =>
                vcpAction.Value.Where(vcp => vcp.Key == VCP_PROPS.INPUT_SELECT.codeName).Count() > 0
            ).Count();

            if (monitorsSelectedAsWanted == monitorsWhereSelectionWanted)
                action.svgIcon.GetElementById("monitor_outline").Fill = new SvgColourServer(Color.LimeGreen);
            if (monitorsSelectedAsWanted < monitorsWhereSelectionWanted)
                action.svgIcon.GetElementById("monitor_outline").Fill = new SvgColourServer(Color.White);

            // The power mode byte is not guaranteed to be a known enum value.
            var powerColor = POWERSTATUS_COLORS.TryGetValue(finalPowerStatus.Power, out var knownColor)
                ? knownColor
                : POWERSTATUS_COLORS[MonitorStatus.PowerStatus.UNKNOWN];

            var powerElement = action.svgIcon.GetElementById("monitor_button");
            if (powerElement == null)
                return;

            // The power indicator is either the original filled dot, the stroked
            // IEC symbol (power-mode buttons) or the brightness sun (filled disc
            // plus stroked rays); recolor whichever paint servers it uses.
            if (powerElement.Fill == SvgPaintServer.None)
            {
                powerElement.Stroke = new SvgColourServer(powerColor);
            }
            else
            {
                powerElement.Fill = new SvgColourServer(powerColor);
                if (powerElement.Stroke != null && powerElement.Stroke != SvgPaintServer.None)
                {
                    powerElement.Stroke = new SvgColourServer(powerColor);
                }
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
                if (action.Settings.Properties.Count == 0)
                    continue;

                var configuredModels = action.Settings.Properties.Keys.ToList();

                // Under DDC/CI contention a configured monitor can transiently be
                // missing from the detected set; keep the last icon instead of crashing.
                if (!configuredModels.All(model => monitorStates.ContainsKey(model)))
                    continue;

                ReadContinuousValues(action, monitorStates);
                RefreshValueGlyph(action, monitorStates);
                ApplyIconColors(action, monitorStates);
                action.PushIcon();
            }

        }

    }
}
