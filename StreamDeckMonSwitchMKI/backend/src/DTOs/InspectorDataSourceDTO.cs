using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StreamDeckMonSwitchMKI.dtos
{
    public interface IInspectorDataSourceItemDTO { }
    public class InspectorDataSourceItemGroupDTO : IInspectorDataSourceItemDTO
    {
        [JsonProperty("label", DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        public string Label { get; set; }

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

    public class InspectorDataSourceItemDTO : IInspectorDataSourceItemDTO
    {
        [JsonProperty("disabled", DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        public bool Disabled { get; set; }

        [JsonProperty("label", DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        public string Label { get; set; }

        [JsonProperty("value")]
        public string Value { get; set; }


        public InspectorDataSourceItemDTO(string value, string label = null, bool disabled = false)
        {
            Value = value;
            Label = label;
            Disabled = disabled;
        }
    }

    public class InspectorDataSourceDTO
    {
        [JsonProperty("event")]
        public string Event { get; set; }

        [JsonProperty("items")]
        public List<IInspectorDataSourceItemDTO> Items { get; set; } = new List<IInspectorDataSourceItemDTO>();


        public InspectorDataSourceDTO(string event_, List<IInspectorDataSourceItemDTO> items)
        {
            Event = event_;
            Items = items;
        }

    }
}
