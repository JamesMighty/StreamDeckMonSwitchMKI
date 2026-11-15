namespace StreamDeckMonitorSwitch
{
    /**
     * Central registry for all magic strings used across the plugin.
     * Grouped by domain for discoverability.
     */
    public static class Constants
    {
        // --- Plugin identity ---
        public const string ACTION_UUID = "com.elgato.streamdeck.streamdeckmonswitch.monitorpluginaction";

        // --- SVG element IDs ---
        public static class SvgIds
        {
            public const string MONITOR_OUTLINE = "monitor_outline";
            public const string MONITOR_BUTTON = "monitor_button";
            public const string MAIN_LAYER = "main_layer";
            public const string BACKGROUND_LAYER = "backgroound_layer";
            public const string VCP_GLYPH = "vcp_glyph";
        }

        // --- Icon asset paths ---
        public static class Assets
        {
            public const string MONITOR_ICON_SVG = "img/computer-monitor-white.svg";
        }

        // --- PI event names (sendToPlugin / sendToPropertyInspector) ---
        public static class PiEvents
        {
            public const string GET_MONITORS = "getMonitors";
            public const string GET_PROPERTIES = "getProperties";
            public const string GET_VALUES = "getValues";
            public const string GET_VCP_INFO = "getVcpInfo";
        }

        // --- Settings JSON property names ---
        public static class SettingsKeys
        {
            public const string MONITORS = "monitors";
            public const string PROPERTIES = "properties";
            public const string VALUES = "values";
            public const string ACTION_MODE = "actionMode";
            public const string STEP_SIZE = "stepSize";
            public const string WRAP_AROUND = "wrapAround";
            public const string TARGET_VALUE = "targetValue";
        }

        // --- VCP property type descriptors ---
        public static class VcpTypes
        {
            public const string DISCRETE = "discrete";
            public const string CONTINUOUS = "continuous";
        }

        // --- Environment variables ---
        public static class EnvVars
        {
            public const string UPDATE_PERIOD_MS = "SDMONSWITCH_UPDATE_PERIOD_MS";
        }

        // --- Capabilities string keys ---
        public static class CapsKeys
        {
            public const string CMDS = "cmds";
            public const string VCP = "vcp";
        }
    }
}
