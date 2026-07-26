using CocoroConsole.Models.OtomeKairoApi;
using CocoroConsole.Services;
using System;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
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

                UpdateOverview(snapshot);
                CurrentStateTextBox.Text = PrettyJson(snapshot.CurrentState);
                RuntimeDetailTextBox.Text = JsonSerializer.Serialize(
                    new
                    {
                        generated_at = snapshot.GeneratedAt,
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
            SetOverviewMessage(message);
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

        private void UpdateOverview(OtomeKairoCurrentStateSnapshot snapshot)
        {
            var builder = new StringBuilder();
            var currentState = snapshot.CurrentState;

            AppendArraySection(
                builder,
                "前景世界状態",
                TryGetProperty(currentState, "foreground_world_states"),
                element =>
                    $"種別={GetString(element, "state_type")} 対象={GetString(element, "scope")} " +
                    $"顕著度={GetString(element, "salience")} 要約={GetString(element, "summary_text")}"
            );
            builder.AppendLine("人物別活動状態:");
            var activityContexts = TryGetProperty(currentState, "activity_contexts");
            if (activityContexts.ValueKind != JsonValueKind.Array || activityContexts.GetArrayLength() == 0)
            {
                builder.AppendLine("  （なし）");
            }
            else
            {
                var shownCount = 0;
                foreach (var activityContext in activityContexts.EnumerateArray())
                {
                    if (shownCount >= 5)
                    {
                        break;
                    }

                    var currentActivity = TryGetProperty(activityContext, "current_activity");
                    var previousActivity = TryGetProperty(activityContext, "previous_activity");
                    var actor = FirstNonEmpty(
                        GetString(currentActivity, "actor"),
                        GetString(previousActivity, "actor"));
                    builder.AppendLine($"  - 人物={DisplayOptional(actor)}");
                    AppendActivityLine(builder, "    現在", currentActivity);
                    AppendActivityLine(builder, "    直前", previousActivity);
                    shownCount++;
                }

                var remainingCount = activityContexts.GetArrayLength() - shownCount;
                if (remainingCount > 0)
                {
                    builder.AppendLine($"  他 {remainingCount} 件は詳細タブにあります。");
                }
            }
            SetOverviewPanel(WorldActivityPanel, builder);

            builder.Clear();
            var moodState = TryGetProperty(currentState, "mood_state");
            builder.AppendLine("気分状態:");
            if (moodState.ValueKind != JsonValueKind.Object)
            {
                builder.AppendLine("  （なし）");
            }
            else
            {
                builder.AppendLine($"  信頼度={GetString(moodState, "confidence")}");
                builder.AppendLine($"  VAD={BuildVadLine(TryGetProperty(moodState, "current_vad"))}");
            }
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
                "ドライブ状態",
                TryGetProperty(currentState, "drive_states"),
                element =>
                    $"種別={GetString(element, "drive_kind")} 顕著度={GetString(element, "salience")} " +
                    $"要約={GetString(element, "summary_text")}"
            );
            SetOverviewPanel(InnerStatePanel, builder);

            builder.Clear();
            builder.AppendLine("進行中アクション:");
            var ongoingAction = TryGetProperty(currentState, "ongoing_action");
            if (ongoingAction.ValueKind != JsonValueKind.Object)
            {
                builder.AppendLine("  （なし）");
            }
            else
            {
                builder.AppendLine($"  ID={GetString(ongoingAction, "action_id")}");
                builder.AppendLine($"  状態={GetString(ongoingAction, "status")}");
                builder.AppendLine($"  目標={GetString(ongoingAction, "goal_summary")}");
                builder.AppendLine($"  現在のステップ={GetString(ongoingAction, "step_summary")}");
                builder.AppendLine($"  最終能力ID={GetString(ongoingAction, "last_capability_id")}");
            }
            AppendArraySection(
                builder,
                "自律実行",
                TryGetProperty(currentState, "autonomous_runs"),
                element =>
                    $"ID={GetString(element, "run_id")} 状態={GetString(element, "status")} " +
                    $"目的={GetString(element, "objective_summary")} 現在のステップ={GetString(element, "current_step_summary")}"
            );
            AppendArraySection(
                builder,
                "保留意図候補",
                TryGetProperty(currentState, "pending_intent_candidates"),
                element =>
                    $"種別={GetString(element, "intent_kind")} 実行開始以降={GetString(element, "not_before")} " +
                    $"失効時刻={GetString(element, "expires_at")} 要約={GetString(element, "intent_summary")}"
            );
            SetOverviewPanel(ActionIntentPanel, builder);

            builder.Clear();
            AppendArraySection(
                builder,
                "認識対象",
                TryGetProperty(currentState, "entity_registry"),
                element =>
                    $"対象={GetString(element, "display_name")} 種別={GetString(element, "entity_type")} " +
                    $"参照={GetString(element, "entity_ref")} 信頼度={GetString(element, "confidence")} " +
                    $"顕著度={GetString(element, "salience")}"
            );
            AppendArraySection(
                builder,
                "関係",
                TryGetProperty(currentState, "relation_index"),
                element =>
                    $"{GetString(element, "source_ref")} → {GetString(element, "target_ref")} " +
                    $"関係={GetString(element, "relation_predicate")} 状態={GetString(element, "derived_status")} " +
                    $"要約={GetString(element, "representative_summary")}"
            );
            SetOverviewPanel(EntityRelationPanel, builder);

            builder.Clear();
            var visualDailySummary = TryGetProperty(currentState, "visual_daily_summary");
            if (visualDailySummary.ValueKind != JsonValueKind.Object)
            {
                builder.AppendLine("（なし）");
            }
            else
            {
                builder.AppendLine($"対象日: {GetString(visualDailySummary, "latest_local_date")}");
                builder.AppendLine($"digest ID: {GetString(visualDailySummary, "latest_digest_id")}");
                builder.AppendLine($"記録数: {GetString(visualDailySummary, "record_count")}");
                builder.AppendLine($"グループ数: {GetString(visualDailySummary, "group_count")}");
                builder.AppendLine($"保持数: {GetString(visualDailySummary, "retained_count")}");
                builder.AppendLine($"圧縮数: {GetString(visualDailySummary, "compressed_count")}");
                builder.AppendLine($"記憶候補数: {GetString(visualDailySummary, "memory_candidate_count")}");
            }
            VisualSummaryTextBlock.Text = CleanOverviewText(builder);

            builder.Clear();
            builder.AppendLine($"接続状態: {GetString(snapshot.RuntimeSummary, "connection_state")}");
            builder.AppendLine(
                $"定期思考スケジューラ: {GetBooleanLabel(snapshot.RuntimeSummary, "background_thinking_scheduler_active", "稼働", "停止")}"
            );
            builder.AppendLine(
                $"自律実行スケジューラ: {GetBooleanLabel(snapshot.RuntimeSummary, "autonomous_run_scheduler_active", "稼働", "停止")}"
            );
            builder.AppendLine(
                $"記憶ジョブワーカー: {GetBooleanLabel(snapshot.RuntimeSummary, "memory_job_worker_active", "稼働", "停止")}"
            );
            builder.AppendLine(
                $"視覚日次整理ワーカー: {GetBooleanLabel(snapshot.RuntimeSummary, "visual_daily_worker_active", "稼働", "停止")}"
            );
            builder.AppendLine($"保留中記憶ジョブ数: {GetString(snapshot.RuntimeSummary, "pending_memory_job_count")}");
            builder.AppendLine(
                $"記憶ジョブ処理: {GetBooleanLabel(snapshot.RuntimeSummary, "memory_job_in_progress", "実行中", "待機中")}"
            );
            builder.AppendLine(
                $"視覚日次整理: {GetBooleanLabel(snapshot.RuntimeSummary, "visual_daily_in_progress", "実行中", "待機中")}"
            );
            var wakeRuntimeState = TryGetProperty(snapshot.RuntimeDetail, "wake_runtime_state");
            builder.AppendLine($"最終判断時刻: {DisplayOptional(GetString(wakeRuntimeState, "last_wake_at"))}");
            builder.AppendLine($"最終自発発話時刻: {DisplayOptional(GetString(wakeRuntimeState, "last_spontaneous_at"))}");
            builder.AppendLine($"初回待機終了時刻: {DisplayOptional(GetString(wakeRuntimeState, "initial_delay_until"))}");
            builder.AppendLine($"再試行時刻: {DisplayOptional(GetString(wakeRuntimeState, "retry_after"))}");
            builder.AppendLine($"発話履歴数: {GetString(wakeRuntimeState, "speech_history_count")}");
            builder.AppendLine(
                $"記憶後処理サイクル: {DisplayOptional(GetString(TryGetProperty(snapshot.RuntimeDetail, "memory_postprocess_runtime_state"), "current_cycle_id"))}"
            );
            builder.AppendLine(
                $"視覚日次整理digest: {DisplayOptional(GetString(TryGetProperty(snapshot.RuntimeDetail, "visual_daily_runtime_state"), "current_digest_id"))}"
            );
            SetOverviewPanel(RuntimeOverviewPanel, builder);

            builder.Clear();
            AppendArraySection(
                builder,
                "能力一覧",
                TryGetProperty(snapshot.CapabilityInspection, "capabilities"),
                element =>
                    $"能力ID={GetString(element, "capability_id")} " +
                    $"利用={GetBooleanLabel(element, "available", "可能", "不可")} " +
                    $"実行={GetBooleanLabel(TryGetProperty(element, "state"), "busy", "実行中", "待機中")} " +
                    $"一時停止={GetBooleanLabel(TryGetProperty(element, "state"), "paused", "あり", "なし")} " +
                    $"理由={DisplayOptional(GetString(element, "unavailable_reason"))}"
            );
            AppendArraySection(
                builder,
                "保留中能力要求",
                TryGetProperty(snapshot.RuntimeDetail, "pending_capability_requests"),
                element =>
                    $"能力ID={GetString(element, "capability_id")} 要求ID={GetString(element, "request_id")} " +
                    $"対象={GetString(element, "target_client_id")} 失効時刻={GetString(element, "expires_at")}"
            );
            SetOverviewPanel(CapabilityPanel, builder);

            builder.Clear();
            AppendArraySection(
                builder,
                "定期観測",
                TryGetProperty(snapshot.RuntimeDetail, "wake_policy_observations"),
                element =>
                    $"観測ID={GetString(element, "observation_id")} 最終状態={GetString(element, "last_status")} " +
                    $"最終実行={GetString(element, "last_run_at")} source={GetString(element, "last_vision_source_id")} " +
                    $"画像数={GetString(element, "last_image_count")} 要約={GetString(element, "last_summary")}"
            );
            SetOverviewPanel(ObservationPanel, builder);
        }

        private void SetOverviewMessage(string message)
        {
            WorldActivityPanel.Children.Clear();
            WorldActivityPanel.Children.Add(CreateOverviewTextBlock(message));
            InnerStatePanel.Children.Clear();
            ActionIntentPanel.Children.Clear();
            EntityRelationPanel.Children.Clear();
            VisualSummaryTextBlock.Text = string.Empty;
            RuntimeOverviewPanel.Children.Clear();
            CapabilityPanel.Children.Clear();
            ObservationPanel.Children.Clear();
        }

        private static string CleanOverviewText(StringBuilder builder)
        {
            var text = builder.ToString().Trim();
            return string.IsNullOrWhiteSpace(text) ? "（なし）" : text;
        }

        private void SetOverviewPanel(StackPanel panel, StringBuilder builder)
        {
            panel.Children.Clear();

            var lines = builder
                .ToString()
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                .Select(line => line.TrimStart())
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToList();

            if (lines.Count == 0)
            {
                panel.Children.Add(CreateOverviewTextBlock("（なし）"));
                return;
            }

            foreach (var line in lines)
            {
                if (line.StartsWith("- ", StringComparison.Ordinal))
                {
                    AddBulletOverviewLine(panel, line.Substring(2).TrimStart());
                }
                else
                {
                    panel.Children.Add(CreateOverviewTextBlock(line));
                }
            }
        }

        private void AddBulletOverviewLine(StackPanel panel, string text)
        {
            var grid = new Grid
            {
                Margin = new Thickness(0, 0, 0, 2),
            };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var bulletTextBlock = CreateOverviewTextBlock("-");
            bulletTextBlock.Margin = new Thickness(0, 0, 6, 0);
            Grid.SetColumn(bulletTextBlock, 0);

            var bodyTextBlock = CreateOverviewTextBlock(text);
            Grid.SetColumn(bodyTextBlock, 1);

            grid.Children.Add(bulletTextBlock);
            grid.Children.Add(bodyTextBlock);
            panel.Children.Add(grid);
        }

        private static TextBlock CreateOverviewTextBlock(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontFamily = new System.Windows.Media.FontFamily("Meiryo UI"),
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 21,
            };
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

            var shownCount = 0;
            foreach (var item in array.EnumerateArray())
            {
                if (shownCount >= 5)
                {
                    break;
                }

                builder.AppendLine($"  - {formatter(item)}");
                shownCount++;
            }

            var remainingCount = array.GetArrayLength() - shownCount;
            if (remainingCount > 0)
            {
                builder.AppendLine($"  他 {remainingCount} 件は詳細タブにあります。");
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

        private static string GetBooleanLabel(
            JsonElement element,
            string propertyName,
            string trueLabel,
            string falseLabel)
        {
            return TryGetProperty(element, propertyName).ValueKind switch
            {
                JsonValueKind.True => trueLabel,
                JsonValueKind.False => falseLabel,
                _ => "（不明）",
            };
        }

        private static string DisplayOptional(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "（なし）" : value;
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
