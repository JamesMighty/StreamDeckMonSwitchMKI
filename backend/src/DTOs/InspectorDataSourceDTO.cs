using Newtonsoft.Json;
using System.Collections.Generic;

namespace StreamDeckMonitorSwitch.dtos
{
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
