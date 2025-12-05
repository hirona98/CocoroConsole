using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CocoroConsole.Models.CocoroGhostApi
{
    public class LlmPreset
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("llm_api_key")]
        public string? LlmApiKey { get; set; }

        [JsonPropertyName("llm_model")]
        public string? LlmModel { get; set; }

        [JsonPropertyName("llm_base_url")]
        public string? LlmBaseUrl { get; set; }

        [JsonPropertyName("reasoning_effort")]
        public string? ReasoningEffort { get; set; }

        [JsonPropertyName("max_turns_window")]
        public int? MaxTurnsWindow { get; set; }

        [JsonPropertyName("max_tokens")]
        public int? MaxTokens { get; set; }

        [JsonPropertyName("max_tokens_vision")]
        public int? MaxTokensVision { get; set; }

        [JsonPropertyName("image_model")]
        public string? ImageModel { get; set; }

        [JsonPropertyName("image_model_api_key")]
        public string? ImageModelApiKey { get; set; }

        [JsonPropertyName("image_timeout_seconds")]
        public int? ImageTimeoutSeconds { get; set; }

        [JsonPropertyName("image_llm_base_url")]
        public string? ImageLlmBaseUrl { get; set; }

        [JsonPropertyName("embedding_model")]
        public string? EmbeddingModel { get; set; }

        [JsonPropertyName("embedding_api_key")]
        public string? EmbeddingApiKey { get; set; }

        [JsonPropertyName("embedding_base_url")]
        public string? EmbeddingBaseUrl { get; set; }

        [JsonPropertyName("embedding_dimension")]
        public int? EmbeddingDimension { get; set; }

        [JsonPropertyName("similar_episodes_limit")]
        public int? SimilarEpisodesLimit { get; set; }

        [JsonExtensionData]
        public Dictionary<string, object?>? AdditionalFields { get; set; }
    }
}
