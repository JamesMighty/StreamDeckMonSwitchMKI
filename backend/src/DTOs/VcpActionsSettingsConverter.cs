using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;

namespace StreamDeckMonitorSwitch.dtos
{
    /**
     * <summary>
     * Lenient deserializer for the per-key "values" settings
     * (Dictionary&lt;monitorModel, Dictionary&lt;vcpCodeName, List&lt;int&gt;&gt;&gt;).
     *
     * The Property Inspector checkbox-list persists selections as arrays of strings,
     * e.g. { "MODEL-A": { "Input Select": ["15", "17"] } }.
     * For backward compatibility, single scalars (int | "15") are also accepted and
     * wrapped into a one-element list.
     * </summary>
     */
    public class VcpActionsSettingsConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(Dictionary<string, Dictionary<string, List<int>>>);
        }

        public override bool CanWrite
        {
            get { return false; }
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            var result = new Dictionary<string, Dictionary<string, List<int>>>();
            var token = JToken.Load(reader);

            if (token.Type == JTokenType.Null)
            {
                return result;
            }

            if (token.Type != JTokenType.Object)
            {
                throw new JsonSerializationException("Expected object for VCP actions settings, got: " + token.Type);
            }

            foreach (var monitorProp in token.Children<JProperty>())
            {
                var inner = new Dictionary<string, List<int>>();

                if (monitorProp.Value.Type == JTokenType.Object)
                {
                    foreach (var vcpProp in monitorProp.Value.Children<JProperty>())
                    {
                        inner[vcpProp.Name] = CoerceVcpValues(vcpProp.Value);
                    }
                }

                result[monitorProp.Name] = inner;
            }

            return result;
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            throw new NotImplementedException("VcpActionsSettingsConverter is read-only");
        }

        /**
         * Coerces a leaf token (int | "15" | [15] | ["15"] | [15, 17] | ["15","17"])
         * into a List&lt;int&gt;. Single scalars become one-element lists.
         */
        public static List<int> CoerceVcpValues(JToken token)
        {
            if (token.Type == JTokenType.Array)
            {
                var arr = (JArray)token;
                var values = new List<int>();
                foreach (var item in arr)
                {
                    values.Add(CoerceSingleVcpValue(item));
                }
                return values;
            }

            // Single scalar → wrap in list.
            return new List<int> { CoerceSingleVcpValue(token) };
        }

        /**
         * Coerces a single token (int | "15") into an int.
         */
        public static int CoerceSingleVcpValue(JToken token)
        {
            if (token.Type == JTokenType.Integer)
            {
                return token.Value<int>();
            }

            if (token.Type == JTokenType.String)
            {
                int parsed;
                if (int.TryParse(token.Value<string>(), out parsed))
                {
                    return parsed;
                }
                throw new JsonSerializationException("Could not parse VCP value from string: '" + token.Value<string>() + "'");
            }

            throw new JsonSerializationException("Unsupported VCP value token type: " + token.Type);
        }
    }
}
