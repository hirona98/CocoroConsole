using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CocoroConsole.Models.CocoroGhostApi
{
    public class PersonaPreset
    {
        [JsonPropertyName("persona_preset_id")]
        public string? PersonaPresetId { get; set; }

        [JsonPropertyName("persona_preset_name")]
        public string PersonaPresetName { get; set; } = string.Empty;

        [JsonPropertyName("persona_text")]
        public string PersonaText { get; set; } = string.Empty;

        [JsonExtensionData]
        public Dictionary<string, object?>? AdditionalFields { get; set; }
    }
}
