using System;
using System.Text.Json.Serialization;

namespace CocoroConsole.Models.CocoroGhostApi
{
    /// <summary>
    /// /api/mood/debug のレスポンスDTO。
    /// </summary>
    public class MoodDebugResponse
    {
        /// <summary>
        /// 現在の気分（LongMoodState）。未作成の場合は null。
        /// </summary>
        [JsonPropertyName("mood")]
        public MoodDebugMoodState? Mood { get; set; }

        /// <summary>
        /// 直近の瞬間感情（event_affects）。
        /// </summary>
        [JsonPropertyName("recent_affects")]
        public MoodDebugRecentAffect[] RecentAffects { get; set; } = Array.Empty<MoodDebugRecentAffect>();

        /// <summary>
        /// 返却件数の上限など（limits）。
        /// </summary>
        [JsonPropertyName("limits")]
        public MoodDebugLimits Limits { get; set; } = new MoodDebugLimits();
    }

    /// <summary>
    /// /api/mood/debug の mood 本体。
    /// </summary>
    public class MoodDebugMoodState
    {
        /// <summary>
        /// 状態ID。
        /// </summary>
        [JsonPropertyName("state_id")]
        public int StateId { get; set; }

        /// <summary>
        /// 気分の本文。
        /// </summary>
        [JsonPropertyName("body_text")]
        public string BodyText { get; set; } = string.Empty;

        /// <summary>
        /// 気分の確信度。
        /// </summary>
        [JsonPropertyName("confidence")]
        public double Confidence { get; set; }

        /// <summary>
        /// 気分の重要度。
        /// </summary>
        [JsonPropertyName("salience")]
        public double Salience { get; set; }

        /// <summary>
        /// ベースラインVAD。
        /// </summary>
        [JsonPropertyName("baseline_vad")]
        public MoodDebugVad? BaselineVad { get; set; }

        /// <summary>
        /// ショックVAD（読み出し時点で減衰後）。
        /// </summary>
        [JsonPropertyName("shock_vad")]
        public MoodDebugVad? ShockVad { get; set; }

        /// <summary>
        /// 合成VAD（baseline + shock 等の結果）。
        /// </summary>
        [JsonPropertyName("vad")]
        public MoodDebugVad? Vad { get; set; }

        /// <summary>
        /// サーバが返す「現在時刻」。
        /// </summary>
        [JsonPropertyName("now")]
        public DateTimeOffset Now { get; set; }

        /// <summary>
        /// 最終確認時刻（ISO 8601）。
        /// </summary>
        [JsonPropertyName("last_confirmed_at")]
        public DateTimeOffset LastConfirmedAt { get; set; }

        /// <summary>
        /// now - last_confirmed_at（秒）。
        /// </summary>
        [JsonPropertyName("dt_seconds")]
        public int DtSeconds { get; set; }
    }

    /// <summary>
    /// VAD（Valence / Arousal / Dominance）。
    /// </summary>
    public class MoodDebugVad
    {
        /// <summary>
        /// Valence。
        /// </summary>
        [JsonPropertyName("v")]
        public double V { get; set; }

        /// <summary>
        /// Arousal。
        /// </summary>
        [JsonPropertyName("a")]
        public double A { get; set; }

        /// <summary>
        /// Dominance。
        /// </summary>
        [JsonPropertyName("d")]
        public double D { get; set; }
    }

    /// <summary>
    /// /api/mood/debug の recent_affects 要素。
    /// </summary>
    public class MoodDebugRecentAffect
    {
        /// <summary>
        /// affect のID。
        /// </summary>
        [JsonPropertyName("affect_id")]
        public int AffectId { get; set; }

        /// <summary>
        /// 参照する event のID。
        /// </summary>
        [JsonPropertyName("event_id")]
        public int EventId { get; set; }

        /// <summary>
        /// event のsource（例: chat）。
        /// </summary>
        [JsonPropertyName("event_source")]
        public string EventSource { get; set; } = string.Empty;

        /// <summary>
        /// event の作成時刻。
        /// </summary>
        [JsonPropertyName("event_created_at")]
        public DateTimeOffset EventCreatedAt { get; set; }

        /// <summary>
        /// affect の作成時刻。
        /// </summary>
        [JsonPropertyName("affect_created_at")]
        public DateTimeOffset AffectCreatedAt { get; set; }

        /// <summary>
        /// 瞬間感情の本文。
        /// </summary>
        [JsonPropertyName("moment_affect_text")]
        public string MomentAffectText { get; set; } = string.Empty;

        /// <summary>
        /// 瞬間感情のラベル。
        /// </summary>
        [JsonPropertyName("moment_affect_labels")]
        public string[] MomentAffectLabels { get; set; } = Array.Empty<string>();

        /// <summary>
        /// 瞬間感情のVAD。
        /// </summary>
        [JsonPropertyName("vad")]
        public MoodDebugVad? Vad { get; set; }

        /// <summary>
        /// 瞬間感情の確信度。
        /// </summary>
        [JsonPropertyName("confidence")]
        public double Confidence { get; set; }
    }

    /// <summary>
    /// /api/mood/debug の limits。
    /// </summary>
    public class MoodDebugLimits
    {
        /// <summary>
        /// recent_affects の最大返却件数。
        /// </summary>
        [JsonPropertyName("recent_affects_limit")]
        public int RecentAffectsLimit { get; set; }
    }
}
