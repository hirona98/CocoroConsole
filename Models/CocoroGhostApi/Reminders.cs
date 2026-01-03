using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CocoroConsole.Models.CocoroGhostApi
{
    /// <summary>
    /// /api/reminders/settings のレスポンス DTO。
    /// リマインダー機能の有効/無効、および通知先クライアントIDを表す。
    /// </summary>
    public sealed class CocoroGhostRemindersSettings
    {
        /// <summary>
        /// リマインダー機能が有効か。
        /// </summary>
        [JsonPropertyName("reminders_enabled")]
        public bool RemindersEnabled { get; set; }

        /// <summary>
        /// 通知を受け取るクライアントID。
        /// </summary>
        [JsonPropertyName("target_client_id")]
        public string? TargetClientId { get; set; }
    }

    /// <summary>
    /// /api/reminders/settings 更新（PUT）リクエスト DTO。
    /// </summary>
    public sealed class CocoroGhostRemindersSettingsUpdateRequest
    {
        /// <summary>
        /// リマインダー機能が有効か。
        /// </summary>
        [JsonPropertyName("reminders_enabled")]
        public bool RemindersEnabled { get; set; }

        /// <summary>
        /// 通知先のクライアントID（必須）。
        /// </summary>
        [JsonPropertyName("target_client_id")]
        public string TargetClientId { get; set; } = string.Empty;
    }

    /// <summary>
    /// /api/reminders のレスポンス DTO。
    /// items がリマインダー一覧。
    /// </summary>
    public sealed class CocoroGhostRemindersListResponse
    {
        /// <summary>
        /// リマインダー一覧。
        /// </summary>
        [JsonPropertyName("items")]
        public List<CocoroGhostReminderItem> Items { get; set; } = new List<CocoroGhostReminderItem>();
    }

    /// <summary>
    /// リマインダー 1 件の DTO。
    /// repeat_kind に応じて scheduled_at / time_of_day / weekdays が使用される。
    /// </summary>
    public sealed class CocoroGhostReminderItem
    {
        /// <summary>
        /// リマインダーID（サーバー側の識別子）。
        /// </summary>
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// 有効/無効。
        /// </summary>
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; }

        /// <summary>
        /// 繰り返し種別（"once" | "daily" | "weekly"）。
        /// </summary>
        [JsonPropertyName("repeat_kind")]
        public string RepeatKind { get; set; } = string.Empty;

        /// <summary>
        /// 通知内容（表示/読み上げ等に使用される想定）。
        /// </summary>
        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;

        /// <summary>
        /// 単発の場合の実行日時（ISO 8601、タイムゾーン付き文字列）。
        /// repeat_kind=once のときのみ使用。
        /// </summary>
        [JsonPropertyName("scheduled_at")]
        public string? ScheduledAt { get; set; }

        /// <summary>
        /// 毎日/毎週の時刻（"HH:mm" 形式）。
        /// repeat_kind=daily/weekly のとき使用。
        /// </summary>
        [JsonPropertyName("time_of_day")]
        public string? TimeOfDay { get; set; }

        /// <summary>
        /// 毎週の曜日（"sun".."sat"）。
        /// repeat_kind=weekly のとき使用。
        /// </summary>
        [JsonPropertyName("weekdays")]
        public List<string>? Weekdays { get; set; }

        /// <summary>
        /// 次回発火時刻（Unix time 秒、UTC）。
        /// </summary>
        [JsonPropertyName("next_fire_at_utc")]
        public long? NextFireAtUtc { get; set; }
    }

    /// <summary>
    /// /api/reminders 作成（POST）リクエスト DTO。
    /// repeat_kind に応じて scheduled_at / time_of_day / weekdays を設定する。
    /// </summary>
    public sealed class CocoroGhostReminderCreateRequest
    {
        /// <summary>
        /// 有効/無効（既定: true）。
        /// </summary>
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// 通知内容。
        /// </summary>
        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;

        /// <summary>
        /// 繰り返し種別（"once" | "daily" | "weekly"）。
        /// </summary>
        [JsonPropertyName("repeat_kind")]
        public string RepeatKind { get; set; } = string.Empty;

        /// <summary>
        /// 単発日時（ISO 8601、タイムゾーン付き）。repeat_kind=once のときのみ。
        /// </summary>
        [JsonPropertyName("scheduled_at")]
        public string? ScheduledAt { get; set; }

        /// <summary>
        /// 時刻（"HH:mm"）。repeat_kind=daily/weekly のときのみ。
        /// </summary>
        [JsonPropertyName("time_of_day")]
        public string? TimeOfDay { get; set; }

        /// <summary>
        /// 曜日（"sun".."sat"）。repeat_kind=weekly のときのみ。
        /// </summary>
        [JsonPropertyName("weekdays")]
        public List<string>? Weekdays { get; set; }
    }

    /// <summary>
    /// /api/reminders 作成（POST）レスポンス DTO。
    /// </summary>
    public sealed class CocoroGhostReminderCreateResponse
    {
        /// <summary>
        /// 作成されたリマインダーID。
        /// </summary>
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;
    }

    /// <summary>
    /// /api/reminders/{id} 更新（PATCH）リクエスト DTO。
    /// </summary>
    public sealed class CocoroGhostReminderPatchRequest
    {
        /// <summary>
        /// 有効/無効。
        /// </summary>
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; }

        /// <summary>
        /// 通知内容。
        /// </summary>
        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;

        /// <summary>
        /// 繰り返し種別（"once" | "daily" | "weekly"）。
        /// </summary>
        [JsonPropertyName("repeat_kind")]
        public string RepeatKind { get; set; } = string.Empty;

        /// <summary>
        /// 単発日時（ISO 8601、タイムゾーン付き）。repeat_kind=once のときのみ。
        /// </summary>
        [JsonPropertyName("scheduled_at")]
        public string? ScheduledAt { get; set; }

        /// <summary>
        /// 時刻（"HH:mm"）。repeat_kind=daily/weekly のときのみ。
        /// </summary>
        [JsonPropertyName("time_of_day")]
        public string? TimeOfDay { get; set; }

        /// <summary>
        /// 曜日（"sun".."sat"）。repeat_kind=weekly のときのみ。
        /// </summary>
        [JsonPropertyName("weekdays")]
        public List<string>? Weekdays { get; set; }
    }
}
