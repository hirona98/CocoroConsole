using CocoroConsole.Models.OtomeKairoApi;
using CocoroConsole.Services;
using System;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace CocoroConsole.Windows
{
    /// <summary>
    /// point-in-time の current-state snapshot を確認するビューアー。
    /// </summary>
    public partial class CurrentStateViewerWindow : Window
    {
        private readonly JsonSerializerOptions _jsonSerializerOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        private OtomeKairoApiClient? _apiClient;
        private CancellationTokenSource? _loadCts;
        private readonly DispatcherTimer _autoRefreshTimer;
        private bool _isLoading;

        public bool IsClosed { get; private set; }

        public CurrentStateViewerWindow()
        {
            _autoRefreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3),
            };
            _autoRefreshTimer.Tick += AutoRefreshTimer_Tick;
            InitializeComponent();
            Loaded += CurrentStateViewerWindow_Loaded;
            Closed += CurrentStateViewerWindow_Closed;
        }

        private async void CurrentStateViewerWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadCurrentStateAsync();
            if (!IsClosed && IsAutoRefreshEnabled())
            {
                _autoRefreshTimer.Start();
            }
        }

        private void CurrentStateViewerWindow_Closed(object? sender, EventArgs e)
        {
            IsClosed = true;
            _autoRefreshTimer.Stop();
            _loadCts?.Cancel();
            _loadCts?.Dispose();
            _loadCts = null;
            _apiClient?.Dispose();
            _apiClient = null;
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadCurrentStateAsync();
        }

        private async void AutoRefreshTimer_Tick(object? sender, EventArgs e)
        {
            await LoadCurrentStateAsync();
        }

        private void AutoRefreshCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (IsClosed)
            {
                return;
            }

            var isEnabled = sender is System.Windows.Controls.CheckBox checkBox
                ? checkBox.IsChecked == true
                : IsAutoRefreshEnabled();

            if (isEnabled)
            {
                _autoRefreshTimer.Start();
            }
            else
            {
                _autoRefreshTimer.Stop();
            }
        }

        private async Task LoadCurrentStateAsync()
        {
            if (_isLoading)
            {
                return;
            }

            _isLoading = true;

            try
            {
                var client = EnsureApiClient();
                if (client == null)
                {
                    ClearView("OtomeKairo の token または base URL が未設定です。");
                    return;
                }

                _loadCts?.Cancel();
                _loadCts?.Dispose();
                _loadCts = new CancellationTokenSource();

                var snapshot = await client.GetCurrentStateInspectionAsync(_loadCts.Token).ConfigureAwait(true);

                OverviewTextBox.Text = BuildOverview(snapshot);
                CurrentStateTextBox.Text = PrettyJson(snapshot.CurrentState);
                RuntimeDetailTextBox.Text = JsonSerializer.Serialize(
                    new
                    {
                        generated_at = snapshot.GeneratedAt,
                        settings_snapshot = snapshot.SettingsSnapshot,
                        runtime_summary = snapshot.RuntimeSummary,
                        runtime_detail = snapshot.RuntimeDetail,
                    },
                    _jsonSerializerOptions);
                CapabilityInspectionTextBox.Text = PrettyJson(snapshot.CapabilityInspection);

                UpdateStatus($"現在状態 snapshot を表示中: {snapshot.GeneratedAt} / 自動更新: {AutoRefreshStatusText()}");
            }
            catch (OperationCanceledException)
            {
                UpdateStatus("現在状態 snapshot の読み込みをキャンセルしました。");
            }
            catch (Exception ex)
            {
                UpdateStatus($"現在状態 snapshot の読み込みに失敗しました: {ex.Message}");
                ClearView("現在状態 snapshot の読み込みに失敗しました。");
            }
            finally
            {
                _isLoading = false;
            }
        }

        private OtomeKairoApiClient? EnsureApiClient()
        {
            if (_apiClient != null)
            {
                return _apiClient;
            }

            var appSettings = AppSettings.Instance;
            var baseUrl = appSettings.GetOtomeKairoBaseUrl();
            var token = appSettings.OtomeKairoBearerToken;
            if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(token))
            {
                return null;
            }

            _apiClient = new OtomeKairoApiClient(baseUrl, token);
            return _apiClient;
        }

        private void ClearView(string message)
        {
            OverviewTextBox.Text = message;
            CurrentStateTextBox.Text = string.Empty;
            RuntimeDetailTextBox.Text = string.Empty;
            CapabilityInspectionTextBox.Text = string.Empty;
        }

        private void UpdateStatus(string message)
        {
            StatusTextBlock.Text = message;
        }

        private bool IsAutoRefreshEnabled()
        {
            return AutoRefreshCheckBox.IsChecked == true;
        }

        private string AutoRefreshStatusText()
        {
            return IsAutoRefreshEnabled() ? "3秒" : "停止";
        }

        private string BuildOverview(OtomeKairoCurrentStateSnapshot snapshot)
        {
            var builder = new StringBuilder();
            builder.AppendLine($"生成時刻: {snapshot.GeneratedAt}");
            builder.AppendLine($"選択中人格ID: {GetString(snapshot.SettingsSnapshot, "selected_persona_id")}");
            builder.AppendLine($"選択中記憶セットID: {GetString(snapshot.SettingsSnapshot, "selected_memory_set_id")}");
            builder.AppendLine($"選択中モデルプリセットID: {GetString(snapshot.SettingsSnapshot, "selected_model_preset_id")}");
            builder.AppendLine($"起床モード: {GetString(TryGetProperty(snapshot.SettingsSnapshot, "wake_policy"), "mode")}");
            var visionCaptureCapability = FindCapability(snapshot.CapabilityInspection, "vision.capture");
            builder.AppendLine($"視覚機能利用可: {GetString(visionCaptureCapability, "available")}");
            builder.AppendLine($"視覚source数: {GetArrayLength(TryGetProperty(visionCaptureCapability, "vision_sources"))}");
            builder.AppendLine();

            builder.AppendLine("実行要約:");
            builder.AppendLine($"  接続状態={GetString(snapshot.RuntimeSummary, "connection_state")}");
            builder.AppendLine($"  起床スケジューラ稼働={GetString(snapshot.RuntimeSummary, "wake_scheduler_active")}");
            builder.AppendLine($"  進行中アクションあり={GetString(snapshot.RuntimeSummary, "ongoing_action_exists")}");
            builder.AppendLine($"  記憶ジョブワーカー稼働={GetString(snapshot.RuntimeSummary, "memory_job_worker_active")}");
            builder.AppendLine($"  保留中記憶ジョブ数={GetString(snapshot.RuntimeSummary, "pending_memory_job_count")}");
            builder.AppendLine($"  記憶ジョブ実行中={GetString(snapshot.RuntimeSummary, "memory_job_in_progress")}");
            builder.AppendLine();

            builder.AppendLine("実行詳細:");
            builder.AppendLine(
                $"  起床状態 最終起床={GetString(TryGetProperty(snapshot.RuntimeDetail, "wake_runtime_state"), "last_wake_at")} " +
                $"最終自発発話={GetString(TryGetProperty(snapshot.RuntimeDetail, "wake_runtime_state"), "last_spontaneous_at")} " +
                $"クールダウン終了={GetString(TryGetProperty(snapshot.RuntimeDetail, "wake_runtime_state"), "cooldown_until")}"
            );
            builder.AppendLine(
                $"  記憶後処理 現在サイクルID={GetString(TryGetProperty(snapshot.RuntimeDetail, "memory_postprocess_runtime_state"), "current_cycle_id")}"
            );
            builder.AppendLine(
                $"  保留中機能要求数={GetArrayLength(TryGetProperty(snapshot.RuntimeDetail, "pending_capability_requests"))}"
            );
            builder.AppendLine();

            var currentState = snapshot.CurrentState;
            var ongoingAction = TryGetProperty(currentState, "ongoing_action");
            builder.AppendLine("進行中アクション:");
            if (ongoingAction.ValueKind != JsonValueKind.Object)
            {
                builder.AppendLine("  （なし）");
            }
            else
            {
                builder.AppendLine(
                    $"  アクションID={GetString(ongoingAction, "action_id")} 状態={GetString(ongoingAction, "status")} " +
                    $"最終機能ID={GetString(ongoingAction, "last_capability_id")}"
                );
                builder.AppendLine($"  目標={GetString(ongoingAction, "goal_summary")}");
                builder.AppendLine($"  ステップ={GetString(ongoingAction, "step_summary")}");
            }
            builder.AppendLine();

            builder.AppendLine("気分状態:");
            builder.AppendLine($"  信頼度={GetString(TryGetProperty(currentState, "mood_state"), "confidence")}");
            builder.AppendLine($"  現在VAD={BuildVadLine(TryGetProperty(TryGetProperty(currentState, "mood_state"), "current_vad"))}");
            builder.AppendLine();

            builder.AppendLine("活動状態:");
            var activityContext = TryGetProperty(currentState, "activity_context");
            var currentActivity = TryGetProperty(activityContext, "current_activity");
            var previousActivity = TryGetProperty(activityContext, "previous_activity");
            if (currentActivity.ValueKind != JsonValueKind.Object && previousActivity.ValueKind != JsonValueKind.Object)
            {
                builder.AppendLine("  （なし）");
            }
            else
            {
                AppendActivityLine(builder, "  現在", currentActivity);
                AppendActivityLine(builder, "  直前", previousActivity);
            }
            builder.AppendLine();

            builder.AppendLine("視覚日次要約:");
            var visualDailySummary = TryGetProperty(currentState, "visual_daily_summary");
            if (visualDailySummary.ValueKind != JsonValueKind.Object)
            {
                builder.AppendLine("  （なし）");
            }
            else
            {
                builder.AppendLine(
                    $"  日付={GetString(visualDailySummary, "latest_local_date")} " +
                    $"digest={GetString(visualDailySummary, "latest_digest_id")} " +
                    $"記録={GetString(visualDailySummary, "record_count")} " +
                    $"group={GetString(visualDailySummary, "group_count")} " +
                    $"保持={GetString(visualDailySummary, "retained_count")} " +
                    $"圧縮={GetString(visualDailySummary, "compressed_count")} " +
                    $"記憶候補={GetString(visualDailySummary, "memory_candidate_count")}"
                );
            }
            builder.AppendLine();

            AppendArraySection(
                builder,
                "前景ワールド状態",
                TryGetProperty(currentState, "foreground_world_states"),
                element =>
                    $"種別={GetString(element, "state_type")} 対象={FirstNonEmpty(GetString(element, "scope"), $"{GetString(element, "scope_type")}:{GetString(element, "scope_key")}")} " +
                    $"顕著度={GetString(element, "salience")} 要約={GetString(element, "summary_text")}"
            );
            AppendArraySection(
                builder,
                "ドライブ状態",
                TryGetProperty(currentState, "drive_states"),
                element =>
                    $"種別={GetString(element, "drive_kind")} 顕著度={GetString(element, "salience")} " +
                    $"要約={GetString(element, "summary_text")}"
            );
            AppendArraySection(
                builder,
                "保留意図候補",
                TryGetProperty(currentState, "pending_intent_candidates"),
                element =>
                    $"種別={GetString(element, "intent_kind")} 実行開始以降={GetString(element, "not_before")} " +
                    $"失効時刻={GetString(element, "expires_at")} 要約={GetString(element, "intent_summary")}"
            );
            AppendArraySection(
                builder,
                "保留中機能要求",
                TryGetProperty(snapshot.RuntimeDetail, "pending_capability_requests"),
                element =>
                    $"機能ID={GetString(element, "capability_id")} 要求ID={GetString(element, "request_id")} " +
                    $"対象={GetString(element, "target_client_id")} 失効時刻={GetString(element, "expires_at")}"
            );
            AppendArraySection(
                builder,
                "定期観測",
                TryGetProperty(snapshot.RuntimeDetail, "wake_policy_observations"),
                element =>
                    $"観測ID={GetString(element, "observation_id")} 有効={GetString(element, "enabled")} " +
                    $"間隔={GetString(element, "interval_seconds")}秒 最終状態={GetString(element, "last_status")} " +
                    $"最終実行={GetString(element, "last_run_at")} source={FirstNonEmpty(GetString(element, "last_vision_source_id"), GetString(element, "vision_source_id"))} " +
                    $"画像数={GetString(element, "last_image_count")} 要約={GetString(element, "last_summary")}"
            );
            AppendArraySection(
                builder,
                "感情状態",
                TryGetProperty(currentState, "affect_states"),
                element =>
                    $"ラベル={GetString(element, "affect_label")} 強度={GetString(element, "intensity")} " +
                    $"対象={GetString(element, "target_scope_type")}:{GetString(element, "target_scope_key")} " +
                    $"要約={GetString(element, "summary_text")}"
            );
            AppendArraySection(
                builder,
                "機能一覧",
                TryGetProperty(snapshot.CapabilityInspection, "capabilities"),
                element =>
                    $"機能ID={GetString(element, "capability_id")} 利用可能={GetString(element, "available")} " +
                    $"理由={GetString(element, "unavailable_reason")} 実行中={GetString(TryGetProperty(element, "state"), "busy")} " +
                    $"一時停止={GetString(TryGetProperty(element, "state"), "paused")}"
            );

            return builder.ToString();
        }

        private void AppendArraySection(StringBuilder builder, string title, JsonElement array, Func<JsonElement, string> formatter)
        {
            builder.AppendLine($"{title}:");
            if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() == 0)
            {
                builder.AppendLine("  （なし）");
                builder.AppendLine();
                return;
            }

            foreach (var item in array.EnumerateArray())
            {
                builder.AppendLine($"  - {formatter(item)}");
            }
            builder.AppendLine();
        }

        private string PrettyJson(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Undefined)
            {
                return "{}";
            }

            return JsonSerializer.Serialize(element, _jsonSerializerOptions);
        }

        private static JsonElement TryGetProperty(JsonElement element, string propertyName)
        {
            if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out var value))
            {
                return value;
            }

            return default;
        }

        private static JsonElement FindCapability(JsonElement capabilityInspection, string capabilityId)
        {
            var capabilities = TryGetProperty(capabilityInspection, "capabilities");
            if (capabilities.ValueKind != JsonValueKind.Array)
            {
                return default;
            }

            foreach (var capability in capabilities.EnumerateArray())
            {
                if (string.Equals(GetString(capability, "capability_id"), capabilityId, StringComparison.Ordinal))
                {
                    return capability;
                }
            }

            return default;
        }

        private static string GetString(JsonElement element, string propertyName)
        {
            return GetElementString(TryGetProperty(element, propertyName));
        }

        private static string GetElementString(JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString() ?? string.Empty,
                JsonValueKind.Number => element.ToString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Null => string.Empty,
                JsonValueKind.Undefined => string.Empty,
                _ => element.ToString(),
            };
        }

        private static int GetArrayLength(JsonElement element)
        {
            return element.ValueKind == JsonValueKind.Array ? element.GetArrayLength() : 0;
        }

        private static string BuildVadLine(JsonElement element)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                return "（なし）";
            }

            return $"快不快={GetString(element, "v")} 覚醒={GetString(element, "a")} 支配={GetString(element, "d")}";
        }

        private static void AppendActivityLine(StringBuilder builder, string label, JsonElement element)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            builder.AppendLine(
                $"{label}: {GetString(element, "label")} " +
                $"対象={GetString(element, "target")} 状態={GetString(element, "status")} " +
                $"確度={GetString(element, "confidence")} 顕著度={GetString(element, "salience")} " +
                $"時期={FirstNonEmpty(GetString(element, "age_label"), GetString(element, "ended_age_label"))} " +
                $"理由={GetString(element, "reason_summary")}"
            );
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value) && value != ":")
                {
                    return value;
                }
            }

            return string.Empty;
        }
    }
}
