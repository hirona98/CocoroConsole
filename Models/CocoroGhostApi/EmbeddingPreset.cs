using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CocoroConsole.Models.CocoroGhostApi
{
    public class EmbeddingPreset
    {
        [JsonPropertyName("embedding_preset_id")]
        public string? EmbeddingPresetId { get; set; }

        [JsonPropertyName("embedding_preset_name")]
        public string EmbeddingPresetName { get; set; } = string.Empty;

        [JsonPropertyName("embedding_model_api_key")]
        public string? EmbeddingModelApiKey { get; set; }

        [JsonPropertyName("embedding_model")]
        public string EmbeddingModel { get; set; } = string.Empty;

        [JsonPropertyName("embedding_base_url")]
        public string? EmbeddingBaseUrl { get; set; }

        [JsonPropertyName("embedding_dimension")]
        public int EmbeddingDimension { get; set; }

        [JsonPropertyName("similar_episodes_limit")]
        public int SimilarEpisodesLimit { get; set; }

        [JsonExtensionData]
        public Dictionary<string, object?>? AdditionalFields { get; set; }
    }
}
