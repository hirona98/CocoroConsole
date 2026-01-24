using System.Text.Json.Serialization;

namespace CocoroConsole.Models.CocoroGhostApi
{
    /// <summary>
    /// CocoroGhost API とやり取りするための Embedding プリセット定義です。
    /// </summary>
    public class EmbeddingPreset
    {
        // ===== 既定値（UIとモデルで共有するため、ここを唯一の定義元にする） =====
        /// <summary>Embedding 次元数の既定値です。</summary>
        public const int DefaultEmbeddingDimension = 3072;

        /// <summary>類似エピソード数の既定値です。</summary>
        public const int DefaultSimilarEpisodesLimit = 40;

        // ===== 生成（既定値の揺れを防ぐ） =====
        /// <summary>
        /// アプリ内で使用する既定値が入った <see cref="EmbeddingPreset"/> を生成します。
        /// </summary>
        /// <remarks>
        /// UI 初期化（ClearSettings）や新規プリセット作成で同じ既定値を使うためのファクトリです。
        /// </remarks>
        public static EmbeddingPreset CreateDefault()
        {
            return new EmbeddingPreset
            {
                // 基本情報
                EmbeddingPresetId = null,
                EmbeddingPresetName = string.Empty,

                // Embedding設定
                EmbeddingModelApiKey = null,
                EmbeddingModel = string.Empty,
                EmbeddingBaseUrl = null,
                EmbeddingDimension = DefaultEmbeddingDimension,
                SimilarEpisodesLimit = DefaultSimilarEpisodesLimit
            };
        }

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
    }
}
