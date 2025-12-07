using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CocoroConsole.Models.CocoroGhostApi
{
    public class CocoroGhostSettings
    {
        [JsonPropertyName("exclude_keywords")]
        public List<string> ExcludeKeywords { get; set; } = new List<string>();

        [JsonPropertyName("llm_preset")]
        public List<LlmPreset> LlmPreset { get; set; } = new List<LlmPreset>();

        [JsonPropertyName("embedding_preset")]
        public List<EmbeddingPreset> EmbeddingPreset { get; set; } = new List<EmbeddingPreset>();
    }

    public class CocoroGhostSettingsUpdateRequest
    {
        [JsonPropertyName("exclude_keywords")]
        public List<string> ExcludeKeywords { get; set; } = new List<string>();

        [JsonPropertyName("llm_preset")]
        public List<LlmPreset> LlmPreset { get; set; } = new List<LlmPreset>();

        [JsonPropertyName("embedding_preset")]
        public List<EmbeddingPreset> EmbeddingPreset { get; set; } = new List<EmbeddingPreset>();
    }
}
