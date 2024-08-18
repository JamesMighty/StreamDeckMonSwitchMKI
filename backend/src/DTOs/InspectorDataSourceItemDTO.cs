using Newtonsoft.Json;

namespace StreamDeckMonitorSwitch.dtos
{
    public class InspectorDataSourceItemDTO : IInspectorDataSourceItemDTO
    {
        [JsonProperty("disabled", DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        public bool Disabled { get; set; } = true;

        [JsonProperty("label", DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        public string Label { get; set; } = string.Empty;

        [JsonProperty("value")]
        public string Value { get; set; }


        public InspectorDataSourceItemDTO(string value, string label = "", bool disabled = false)
        {
            Value = value;
            Label = label;
            Disabled = disabled;
        }
    }
}
