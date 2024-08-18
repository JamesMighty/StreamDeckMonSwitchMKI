using Newtonsoft.Json;
using System.Collections.Generic;

namespace StreamDeckMonitorSwitch.dtos
{
    public class InspectorDataSourceItemGroupDTO : IInspectorDataSourceItemDTO
    {
        [JsonProperty("label", DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        public string Label { get; set; } = string.Empty;

        [JsonProperty("children")]
        public List<IInspectorDataSourceItemDTO> Children { get; set; } = new List<IInspectorDataSourceItemDTO>();

        public InspectorDataSourceItemGroupDTO(string label)
        {
            Label = label;
        }

        public InspectorDataSourceItemGroupDTO(string label, List<IInspectorDataSourceItemDTO> children)
        {
            Label = label;
            Children = children;
        }
    }
}
