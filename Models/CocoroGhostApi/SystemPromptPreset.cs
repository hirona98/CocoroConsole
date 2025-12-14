using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CocoroConsole.Models.CocoroGhostApi
{
    public class SystemPromptPreset
    {
        [JsonPropertyName("system_prompt_preset_id")]
        public string? SystemPromptPresetId { get; set; }

        [JsonPropertyName("system_prompt_preset_name")]
        public string SystemPromptPresetName { get; set; } = string.Empty;

        [JsonPropertyName("system_prompt")]
        public string SystemPrompt { get; set; } = string.Empty;

        [JsonExtensionData]
        public Dictionary<string, object?>? AdditionalFields { get; set; }
    }
}
