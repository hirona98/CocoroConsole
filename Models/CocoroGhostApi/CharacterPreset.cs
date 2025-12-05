using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CocoroConsole.Models.CocoroGhostApi
{
    public class CharacterPreset
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("system_prompt")]
        public string SystemPrompt { get; set; } = string.Empty;

        [JsonPropertyName("memory_id")]
        public string? MemoryId { get; set; }

        [JsonExtensionData]
        public Dictionary<string, object?>? AdditionalFields { get; set; }
    }
}
