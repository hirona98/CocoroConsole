using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CocoroConsole.Models.CocoroGhostApi
{
    public class ContractPreset
    {
        [JsonPropertyName("contract_preset_id")]
        public int ContractPresetId { get; set; }

        [JsonPropertyName("contract_preset_name")]
        public string ContractPresetName { get; set; } = string.Empty;

        [JsonPropertyName("contract_text")]
        public string ContractText { get; set; } = string.Empty;

        [JsonExtensionData]
        public Dictionary<string, object?>? AdditionalFields { get; set; }
    }
}
