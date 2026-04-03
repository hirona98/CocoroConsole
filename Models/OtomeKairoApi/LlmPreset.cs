using System.Text.Json.Serialization;

namespace CocoroConsole.Models.OtomeKairoApi
{
    /// <summary>
    /// OtomeKairo API とやり取りするための LLM プリセット定義です。
    /// </summary>
    public class LlmPreset
    {
        // ===== 既定値（UIとモデルで共有するため、ここを唯一の定義元にする） =====
        /// <summary>会話履歴件数（最大ターンウィンドウ）の既定値です。</summary>
        public const int DefaultMaxTurnsWindow = 10;

        /// <summary>最大トークンの既定値です。</summary>
        public const int DefaultMaxTokens = 4096;

        /// <summary>画像認識時の最大トークンの既定値です。</summary>
        public const int DefaultMaxTokensVision = 4096;

        /// <summary>画像認識のタイムアウト秒数の既定値です。</summary>
        public const int DefaultImageTimeoutSeconds = 60;

        // ===== 生成（既定値の揺れを防ぐ） =====
        /// <summary>
        /// アプリ内で使用する既定値が入った <see cref="LlmPreset"/> を生成します。
        /// </summary>
        /// <remarks>
        /// UI 初期化（ClearSettings）や新規プリセット作成で同じ既定値を使うためのファクトリです。
        /// </remarks>
        public static LlmPreset CreateDefault()
        {
            return new LlmPreset
            {
                // 基本情報
                LlmPresetId = null,
                LlmPresetName = string.Empty,

                // 会話LLM設定
                LlmApiKey = string.Empty,
                LlmModel = string.Empty,
                ReasoningEffort = null,
                ReplyWebSearchEnabled = true,
                LlmBaseUrl = null,
                MaxTurnsWindow = DefaultMaxTurnsWindow,
                MaxTokens = DefaultMaxTokens,

                // 画像認識LLM設定
                ImageModelApiKey = null,
                ImageModel = string.Empty,
                ImageLlmBaseUrl = null,
                MaxTokensVision = DefaultMaxTokensVision,
                ImageTimeoutSeconds = DefaultImageTimeoutSeconds
            };
        }

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

        [JsonPropertyName("reply_web_search_enabled")]
        public bool ReplyWebSearchEnabled { get; set; } = true;

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
    }
}
