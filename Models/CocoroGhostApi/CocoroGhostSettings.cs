using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CocoroConsole.Models.CocoroGhostApi
{
    public class CocoroGhostSettings
    {
        [JsonPropertyName("exclude_keywords")]
        public List<string> ExcludeKeywords { get; set; } = new List<string>();

        [JsonPropertyName("reminders_enabled")]
        public bool RemindersEnabled { get; set; }

        [JsonPropertyName("reminders")]
        public List<CocoroGhostReminder> Reminders { get; set; } = new List<CocoroGhostReminder>();

        [JsonPropertyName("llm_preset")]
        public List<LlmPreset> LlmPreset { get; set; } = new List<LlmPreset>();

        [JsonPropertyName("embedding_preset")]
        public List<EmbeddingPreset> EmbeddingPreset { get; set; } = new List<EmbeddingPreset>();
    }

    public class CocoroGhostReminder
    {
        [JsonPropertyName("scheduled_at")]
        public string ScheduledAt { get; set; } = string.Empty;

        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;
    }

    public class CocoroGhostSettingsUpdateRequest
    {
        [JsonPropertyName("exclude_keywords")]
        public List<string> ExcludeKeywords { get; set; } = new List<string>();

        [JsonPropertyName("reminders_enabled")]
        public bool RemindersEnabled { get; set; }

        [JsonPropertyName("reminders")]
        public List<CocoroGhostReminder> Reminders { get; set; } = new List<CocoroGhostReminder>();

        [JsonPropertyName("llm_preset")]
        public List<LlmPreset> LlmPreset { get; set; } = new List<LlmPreset>();

        [JsonPropertyName("embedding_preset")]
        public List<EmbeddingPreset> EmbeddingPreset { get; set; } = new List<EmbeddingPreset>();
    }
}
