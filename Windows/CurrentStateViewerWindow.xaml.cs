using CocoroConsole.Models.OtomeKairoApi;
using CocoroConsole.Services;
using System;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

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
        private bool _isLoading;

        public bool IsClosed { get; private set; }

        public CurrentStateViewerWindow()
        {
            InitializeComponent();
            Loaded += CurrentStateViewerWindow_Loaded;
            Closed += CurrentStateViewerWindow_Closed;
        }

        private async void CurrentStateViewerWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadCurrentStateAsync();
        }

        private void CurrentStateViewerWindow_Closed(object? sender, EventArgs e)
        {
            IsClosed = true;
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

        private async Task LoadCurrentStateAsync()
        {
            if (_isLoading)
            {
                return;
            }

            _isLoading = true;
            RefreshButton.IsEnabled = false;

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

                UpdateStatus("現在状態 snapshot を読み込み中...");
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

                UpdateStatus($"現在状態 snapshot を表示中: {snapshot.GeneratedAt}");
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
                RefreshButton.IsEnabled = true;
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

        private string BuildOverview(OtomeKairoCurrentStateSnapshot snapshot)
        {
            var builder = new StringBuilder();
            builder.AppendLine($"generated_at: {snapshot.GeneratedAt}");
            builder.AppendLine($"selected_persona_id: {GetString(snapshot.SettingsSnapshot, "selected_persona_id")}");
            builder.AppendLine($"selected_memory_set_id: {GetString(snapshot.SettingsSnapshot, "selected_memory_set_id")}");
            builder.AppendLine($"selected_model_preset_id: {GetString(snapshot.SettingsSnapshot, "selected_model_preset_id")}");
            builder.AppendLine($"wake_mode: {GetString(TryGetProperty(snapshot.SettingsSnapshot, "wake_policy"), "mode")}");
            builder.AppendLine($"desktop_watch_enabled: {GetString(TryGetProperty(snapshot.SettingsSnapshot, "desktop_watch"), "enabled")}");
            builder.AppendLine();

            builder.AppendLine("runtime_summary:");
            builder.AppendLine($"  connection_state={GetString(snapshot.RuntimeSummary, "connection_state")}");
            builder.AppendLine($"  wake_scheduler_active={GetString(snapshot.RuntimeSummary, "wake_scheduler_active")}");
            builder.AppendLine($"  ongoing_action_exists={GetString(snapshot.RuntimeSummary, "ongoing_action_exists")}");
            builder.AppendLine($"  memory_job_worker_active={GetString(snapshot.RuntimeSummary, "memory_job_worker_active")}");
            builder.AppendLine($"  pending_memory_job_count={GetString(snapshot.RuntimeSummary, "pending_memory_job_count")}");
            builder.AppendLine($"  memory_job_in_progress={GetString(snapshot.RuntimeSummary, "memory_job_in_progress")}");
            builder.AppendLine();

            builder.AppendLine("runtime_detail:");
            builder.AppendLine(
                $"  wake last_wake_at={GetString(TryGetProperty(snapshot.RuntimeDetail, "wake_runtime_state"), "last_wake_at")} " +
                $"last_spontaneous_at={GetString(TryGetProperty(snapshot.RuntimeDetail, "wake_runtime_state"), "last_spontaneous_at")} " +
                $"cooldown_until={GetString(TryGetProperty(snapshot.RuntimeDetail, "wake_runtime_state"), "cooldown_until")}"
            );
            builder.AppendLine(
                $"  desktop_watch last_watch_at={GetString(TryGetProperty(snapshot.RuntimeDetail, "desktop_watch_runtime_state"), "last_watch_at")}"
            );
            builder.AppendLine(
                $"  memory_postprocess current_cycle_id={GetString(TryGetProperty(snapshot.RuntimeDetail, "memory_postprocess_runtime_state"), "current_cycle_id")}"
            );
            builder.AppendLine(
                $"  pending_capability_request_count={GetArrayLength(TryGetProperty(snapshot.RuntimeDetail, "pending_capability_requests"))}"
            );
            builder.AppendLine();

            var currentState = snapshot.CurrentState;
            var ongoingAction = TryGetProperty(currentState, "ongoing_action");
            builder.AppendLine("ongoing_action:");
            if (ongoingAction.ValueKind != JsonValueKind.Object)
            {
                builder.AppendLine("  (none)");
            }
            else
            {
                builder.AppendLine(
                    $"  action_id={GetString(ongoingAction, "action_id")} status={GetString(ongoingAction, "status")} " +
                    $"last_capability_id={GetString(ongoingAction, "last_capability_id")}"
                );
                builder.AppendLine($"  goal={GetString(ongoingAction, "goal_summary")}");
                builder.AppendLine($"  step={GetString(ongoingAction, "step_summary")}");
            }
            builder.AppendLine();

            builder.AppendLine("mood_state:");
            builder.AppendLine($"  confidence={GetString(TryGetProperty(currentState, "mood_state"), "confidence")}");
            builder.AppendLine($"  current_vad={BuildVadLine(TryGetProperty(TryGetProperty(currentState, "mood_state"), "current_vad"))}");
            builder.AppendLine();

            AppendArraySection(
                builder,
                "foreground_world_states",
                TryGetProperty(currentState, "foreground_world_states"),
                element =>
                    $"{GetString(element, "state_type")} {GetString(element, "scope_type")}:{GetString(element, "scope_key")} " +
                    $"salience={GetString(element, "salience")} {GetString(element, "summary_text")}"
            );
            AppendArraySection(
                builder,
                "drive_states",
                TryGetProperty(currentState, "drive_states"),
                element =>
                    $"{GetString(element, "drive_kind")} salience={GetString(element, "salience")} " +
                    $"{GetString(element, "summary_text")}"
            );
            AppendArraySection(
                builder,
                "pending_intent_candidates",
                TryGetProperty(currentState, "pending_intent_candidates"),
                element =>
                    $"{GetString(element, "intent_kind")} not_before={GetString(element, "not_before")} " +
                    $"expires_at={GetString(element, "expires_at")} {GetString(element, "intent_summary")}"
            );
            AppendArraySection(
                builder,
                "pending_capability_requests",
                TryGetProperty(snapshot.RuntimeDetail, "pending_capability_requests"),
                element =>
                    $"{GetString(element, "capability_id")} request_id={GetString(element, "request_id")} " +
                    $"target={GetString(element, "target_client_id")} expires_at={GetString(element, "expires_at")}"
            );
            AppendArraySection(
                builder,
                "affect_states",
                TryGetProperty(currentState, "affect_states"),
                element =>
                    $"{GetString(element, "affect_label")} intensity={GetString(element, "intensity")} " +
                    $"{GetString(element, "target_scope_type")}:{GetString(element, "target_scope_key")} " +
                    $"{GetString(element, "summary_text")}"
            );
            AppendArraySection(
                builder,
                "capabilities",
                TryGetProperty(snapshot.CapabilityInspection, "capabilities"),
                element =>
                    $"{GetString(element, "capability_id")} available={GetString(element, "available")} " +
                    $"reason={GetString(element, "unavailable_reason")} busy={GetString(TryGetProperty(element, "state"), "busy")} " +
                    $"paused={GetString(TryGetProperty(element, "state"), "paused")}"
            );

            return builder.ToString();
        }

        private void AppendArraySection(StringBuilder builder, string title, JsonElement array, Func<JsonElement, string> formatter)
        {
            builder.AppendLine($"{title}:");
            if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() == 0)
            {
                builder.AppendLine("  (none)");
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
                return "(none)";
            }

            return $"valence={GetString(element, "valence")} arousal={GetString(element, "arousal")} dominance={GetString(element, "dominance")}";
        }
    }
}
