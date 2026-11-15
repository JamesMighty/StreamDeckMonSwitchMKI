using System.Collections.Generic;
using Svg;

namespace StreamDeckMonitorSwitch.Abstraction
{
    /**
     * Abstraction over icon generation and refresh logic.
     * Allows testing icon composition without Stream Deck connectivity.
     */
    public interface IIconRenderer
    {
        /**
         * Builds the full SVG icon for a given action configuration.
         */
        SvgDocument MakeIcon(MonitorPluginAction action);

        /**
         * Refreshes an existing icon's dynamic elements (colors, value glyphs)
         * based on current monitor state.
         */
        void RefreshActionIcon(MonitorPluginAction action);
    }
}
