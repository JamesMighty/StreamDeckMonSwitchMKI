using System;
using System.Collections.Generic;

namespace StreamDeckMonitorSwitch
{
    /**
     * Pure index logic for the button action modes. Kept separate from
     * MonitorPluginAction so it can be unit-tested without the Stream Deck
     * Tools dependency.
     */
    public static class ActionModes
    {
        public const string SET = "set";
        public const string TOGGLE = "toggle";
        public const string LOOP = "loop";
        public const string SCROLL_UP = "scrollUp";
        public const string SCROLL_DOWN = "scrollDown";

        /**
         * Given the configured values, the value currently active on the monitor
         * and the mode, returns the value to write next.
         *
         *   set:    always the first configured value.
         *   toggle: current == first -> second, otherwise first.
         *   loop:   the value after the current one, wrapping; unknown current -> first.
         */
        public static int ResolveNextValue(IList<int> values, int currentValue, string mode)
        {
            if (values == null || values.Count == 0)
            {
                throw new ArgumentException("values must not be empty", "values");
            }

            var index = values.IndexOf(currentValue);

            if (mode == TOGGLE)
            {
                // With a single configured value there is nothing to toggle to.
                if (values.Count < 2)
                {
                    return values[0];
                }
                return index == 0 ? values[1] : values[0];
            }

            if (mode == LOOP)
            {
                return index < 0 ? values[0] : values[(index + 1) % values.Count];
            }

            return values[0];
        }

        /**
         * Computes the next value for a continuous (scroll) control.
         *
         *   scrollUp:   current + step, clamped to max (or wrapped to min).
         *   scrollDown: current - step, clamped to min (or wrapped to max).
         */
        public static int ResolveScrollValue(int currentValue, int stepSize, int minValue, int maxValue, string mode, bool wrapAround)
        {
            if (stepSize < 1) stepSize = 1;

            if (mode == SCROLL_UP)
            {
                var next = currentValue + stepSize;
                if (next > maxValue)
                {
                    return wrapAround ? minValue : maxValue;
                }
                return next;
            }

            if (mode == SCROLL_DOWN)
            {
                var next = currentValue - stepSize;
                if (next < minValue)
                {
                    return wrapAround ? maxValue : minValue;
                }
                return next;
            }

            return currentValue;
        }
    }
}
