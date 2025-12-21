using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CocoroConsole.Models.CocoroGhostApi
{
    public class CocoroGhostSettings
    {
        [JsonPropertyName("exclude_keywords")]
        public List<string> ExcludeKeywords { get; set; } = new List<string>();

        [JsonPropertyName("memory_enabled")]
        public bool MemoryEnabled { get; set; }

        [JsonPropertyName("reminders_enabled")]
        public bool RemindersEnabled { get; set; }

        [JsonPropertyName("reminders")]
        public List<CocoroGhostReminder> Reminders { get; set; } = new List<CocoroGhostReminder>();

        [JsonPropertyName("active_llm_preset_id")]
        public string? ActiveLlmPresetId { get; set; }

        [JsonPropertyName("active_embedding_preset_id")]
        public string? ActiveEmbeddingPresetId { get; set; }

        [JsonPropertyName("active_persona_preset_id")]
        public string? ActivePersonaPresetId { get; set; }

        [JsonPropertyName("active_contract_preset_id")]
        public string? ActiveContractPresetId { get; set; }

        [JsonPropertyName("llm_preset")]
        public List<LlmPreset> LlmPreset { get; set; } = new List<LlmPreset>();

        [JsonPropertyName("embedding_preset")]
        public List<EmbeddingPreset> EmbeddingPreset { get; set; } = new List<EmbeddingPreset>();

        [JsonPropertyName("persona_preset")]
        public List<PersonaPreset> PersonaPreset { get; set; } = new List<PersonaPreset>();

        [JsonPropertyName("contract_preset")]
        public List<ContractPreset> ContractPreset { get; set; } = new List<ContractPreset>();
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

        [JsonPropertyName("memory_enabled")]
        public bool MemoryEnabled { get; set; }

        [JsonPropertyName("reminders_enabled")]
        public bool RemindersEnabled { get; set; }

        [JsonPropertyName("reminders")]
        public List<CocoroGhostReminder> Reminders { get; set; } = new List<CocoroGhostReminder>();

        [JsonPropertyName("active_llm_preset_id")]
        public string ActiveLlmPresetId { get; set; } = string.Empty;

        [JsonPropertyName("active_embedding_preset_id")]
        public string ActiveEmbeddingPresetId { get; set; } = string.Empty;

        [JsonPropertyName("active_persona_preset_id")]
        public string ActivePersonaPresetId { get; set; } = string.Empty;

        [JsonPropertyName("active_contract_preset_id")]
        public string ActiveContractPresetId { get; set; } = string.Empty;

        [JsonPropertyName("llm_preset")]
        public List<LlmPreset> LlmPreset { get; set; } = new List<LlmPreset>();

        [JsonPropertyName("embedding_preset")]
        public List<EmbeddingPreset> EmbeddingPreset { get; set; } = new List<EmbeddingPreset>();

        [JsonPropertyName("persona_preset")]
        public List<PersonaPreset> PersonaPreset { get; set; } = new List<PersonaPreset>();

        [JsonPropertyName("contract_preset")]
        public List<ContractPreset> ContractPreset { get; set; } = new List<ContractPreset>();
    }
}
