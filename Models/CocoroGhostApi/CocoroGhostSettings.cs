using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CocoroConsole.Models.CocoroGhostApi
{
    public class CocoroGhostSettings
    {
        [JsonPropertyName("exclude_keywords")]
        public List<string> ExcludeKeywords { get; set; } = new List<string>();

        [JsonPropertyName("active_llm_preset_id")]
        public string? ActiveLlmPresetId { get; set; }

        [JsonPropertyName("active_character_preset_id")]
        public string? ActiveCharacterPresetId { get; set; }
    }

    public class CocoroGhostSettingsUpdateRequest
    {
        [JsonPropertyName("exclude_keywords")]
        public List<string> ExcludeKeywords { get; set; } = new List<string>();
    }
}
