using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CocoroConsole.Models.OtomeKairoApi
{
    /// <summary>
    /// OtomeKairo の設定 bundle を表します。
    /// </summary>
    public class OtomeKairoEditorState
    {
        // Current
        [JsonPropertyName("current")]
        public OtomeKairoCurrentSettings Current { get; set; } = new OtomeKairoCurrentSettings();

        // Resources
        [JsonPropertyName("personas")]
        public List<OtomeKairoPersonaDefinition> Personas { get; set; } = new List<OtomeKairoPersonaDefinition>();

        [JsonPropertyName("memory_sets")]
        public List<OtomeKairoMemorySetDefinition> MemorySets { get; set; } = new List<OtomeKairoMemorySetDefinition>();

        [JsonPropertyName("model_presets")]
        public List<OtomeKairoModelPresetDefinition> ModelPresets { get; set; } = new List<OtomeKairoModelPresetDefinition>();
    }

    /// <summary>
    /// OtomeKairo の現在設定を表します。
    /// </summary>
    public class OtomeKairoCurrentSettings
    {
        // Identity
        [JsonPropertyName("selected_persona_id")]
        public string SelectedPersonaId { get; set; } = string.Empty;

        [JsonPropertyName("selected_memory_set_id")]
        public string SelectedMemorySetId { get; set; } = string.Empty;

        [JsonPropertyName("selected_model_preset_id")]
        public string SelectedModelPresetId { get; set; } = string.Empty;

        [JsonPropertyName("wake_policy")]
        public Dictionary<string, object?> WakePolicy { get; set; } = new Dictionary<string, object?>();
    }

    /// <summary>
    /// 人格設定を表します。
    /// </summary>
    public class OtomeKairoPersonaDefinition
    {
        // Identity
        [JsonPropertyName("persona_id")]
        public string PersonaId { get; set; } = string.Empty;

        [JsonPropertyName("display_name")]
        public string DisplayName { get; set; } = string.Empty;

        [JsonPropertyName("initiative_baseline")]
        public string InitiativeBaseline { get; set; } = "medium";

        [JsonPropertyName("reference_style")]
        public OtomeKairoPersonaReferenceStyle ReferenceStyle { get; set; } = new OtomeKairoPersonaReferenceStyle();

        [JsonPropertyName("persona_prompt")]
        public string PersonaPrompt { get; set; } = string.Empty;

        [JsonPropertyName("expression_addon")]
        public string ExpressionAddon { get; set; } = string.Empty;
    }

    public class OtomeKairoPersonaReferenceStyle
    {
        [JsonPropertyName("user_natural_reference")]
        public string UserNaturalReference { get; set; } = "マスター";
    }

    /// <summary>
    /// 記憶集合設定を表します。
    /// </summary>
    public class OtomeKairoMemorySetDefinition
    {
        // Fields
        [JsonPropertyName("memory_set_id")]
        public string MemorySetId { get; set; } = string.Empty;

        [JsonPropertyName("display_name")]
        public string DisplayName { get; set; } = string.Empty;

        [JsonPropertyName("embedding")]
        public Dictionary<string, object?> Embedding { get; set; } = new Dictionary<string, object?>();
    }

    /// <summary>
    /// モデルプリセット設定を表します。
    /// </summary>
    public class OtomeKairoModelPresetDefinition
    {
        // Fields
        [JsonPropertyName("model_preset_id")]
        public string ModelPresetId { get; set; } = string.Empty;

        [JsonPropertyName("display_name")]
        public string DisplayName { get; set; } = string.Empty;

        [JsonPropertyName("prompt_window")]
        public OtomeKairoPromptWindowDefinition PromptWindow { get; set; } = new OtomeKairoPromptWindowDefinition();

        [JsonPropertyName("roles")]
        public Dictionary<string, Dictionary<string, object?>> Roles { get; set; } = new Dictionary<string, Dictionary<string, object?>>();
    }

    public class OtomeKairoPromptWindowDefinition
    {
        [JsonPropertyName("recent_turn_limit")]
        public int RecentTurnLimit { get; set; }

        [JsonPropertyName("recent_turn_minutes")]
        public int RecentTurnMinutes { get; set; }
    }

    public class OtomeKairoCameraSourcesEditorState
    {
        [JsonPropertyName("camera_sources")]
        public List<OtomeKairoCameraSourceDefinition> CameraSources { get; set; } = new List<OtomeKairoCameraSourceDefinition>();
    }

    public class OtomeKairoCameraSourceDefinition
    {
        [JsonPropertyName("vision_source_id")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? VisionSourceId { get; set; }

        [JsonPropertyName("connector_kind")]
        public string ConnectorKind { get; set; } = "tapo_c220";

        [JsonPropertyName("client_id")]
        public string ClientId { get; set; } = "tapo-c220-connector-main";

        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; }

        [JsonPropertyName("label")]
        public string Label { get; set; } = string.Empty;

        [JsonPropertyName("connection")]
        public OtomeKairoCameraSourceConnection Connection { get; set; } = new OtomeKairoCameraSourceConnection();
    }

    public class OtomeKairoCameraSourceConnection
    {
        [JsonPropertyName("host")]
        public string Host { get; set; } = string.Empty;

        [JsonPropertyName("camera_username")]
        public string CameraUsername { get; set; } = string.Empty;

        [JsonPropertyName("camera_password")]
        public string CameraPassword { get; set; } = string.Empty;
    }

    public class OtomeKairoMcpServersEditorState
    {
        [JsonPropertyName("mcp_servers")]
        public List<OtomeKairoMcpServerDefinition> McpServers { get; set; } = new List<OtomeKairoMcpServerDefinition>();
    }

    public class OtomeKairoMcpServerDefinition
    {
        [JsonPropertyName("mcp_server_id")]
        public string McpServerId { get; set; } = string.Empty;

        [JsonPropertyName("connector_kind")]
        public string ConnectorKind { get; set; } = "mcp_client";

        [JsonPropertyName("client_id")]
        public string ClientId { get; set; } = "mcp-client-connector-main";

        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; }

        [JsonPropertyName("transport")]
        public string Transport { get; set; } = "stdio";

        [JsonPropertyName("command")]
        public string Command { get; set; } = string.Empty;

        [JsonPropertyName("args")]
        public List<string> Args { get; set; } = new List<string>();

        [JsonPropertyName("cwd")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Cwd { get; set; }

        [JsonPropertyName("env")]
        public Dictionary<string, string> Env { get; set; } = new Dictionary<string, string>();
    }
}
