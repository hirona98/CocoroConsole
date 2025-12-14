using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CocoroConsole.Models.CocoroGhostApi
{
    public class LlmPreset
    {
        [JsonPropertyName("llm_preset_id")]
        public string? LlmPresetId { get; set; }

        [JsonPropertyName("llm_preset_name")]
        public string LlmPresetName { get; set; } = string.Empty;

        [JsonPropertyName("llm_api_key")]
        public string LlmApiKey { get; set; } = string.Empty;

        [JsonPropertyName("llm_model")]
        public string LlmModel { get; set; } = string.Empty;

        [JsonPropertyName("reasoning_effort")]
        public string? ReasoningEffort { get; set; }

        [JsonPropertyName("llm_base_url")]
        public string? LlmBaseUrl { get; set; }

        [JsonPropertyName("max_turns_window")]
        public int MaxTurnsWindow { get; set; }

        [JsonPropertyName("max_tokens")]
        public int MaxTokens { get; set; }

        [JsonPropertyName("image_model_api_key")]
        public string? ImageModelApiKey { get; set; }

        [JsonPropertyName("image_model")]
        public string ImageModel { get; set; } = string.Empty;

        [JsonPropertyName("image_llm_base_url")]
        public string? ImageLlmBaseUrl { get; set; }

        [JsonPropertyName("max_tokens_vision")]
        public int MaxTokensVision { get; set; }

        [JsonPropertyName("image_timeout_seconds")]
        public int ImageTimeoutSeconds { get; set; }

        [JsonExtensionData]
        public Dictionary<string, object?>? AdditionalFields { get; set; }
    }
}
