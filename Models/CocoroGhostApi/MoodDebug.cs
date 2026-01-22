using System;
using System.Text.Json;
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
        /// 気分の付随情報（任意JSON）。
        /// </summary>
        [JsonPropertyName("payload")]
        public JsonElement Payload { get; set; }

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
}

