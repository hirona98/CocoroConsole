using CocoroConsole.Models.OtomeKairoApi;
using CocoroConsole.Services;
using System;
using System.Collections.Generic;
using System.Text.Encodings.Web;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace CocoroConsole.Windows
{
    /// <summary>
    /// fact_resolution_trace を確認する inspection ビューアー。
    /// </summary>
    public partial class FactResolutionViewerWindow : Window
    {
        private readonly JsonSerializerOptions _jsonSerializerOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        private OtomeKairoApiClient? _apiClient;
        private CancellationTokenSource? _traceLoadCts;
        private CancellationTokenSource? _currentStateLoadCts;
        private bool _isLoadingCycles;
        private bool _isLoadingTrace;
        private bool _isLoadingCurrentState;

        public bool IsClosed { get; private set; }

        public FactResolutionViewerWindow()
        {
            InitializeComponent();
            Loaded += FactResolutionViewerWindow_Loaded;
            Closed += FactResolutionViewerWindow_Closed;
        }

        private async void FactResolutionViewerWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadCycleSummariesAsync(preserveSelection: false);
            await LoadCurrentStateAsync();
        }

        private void FactResolutionViewerWindow_Closed(object? sender, EventArgs e)
        {
            IsClosed = true;
            _traceLoadCts?.Cancel();
            _traceLoadCts?.Dispose();
            _traceLoadCts = null;
            _currentStateLoadCts?.Cancel();
            _currentStateLoadCts?.Dispose();
            _currentStateLoadCts = null;
            _apiClient?.Dispose();
            _apiClient = null;
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadCycleSummariesAsync(preserveSelection: true);
        }

        private async void ReloadTraceButton_Click(object sender, RoutedEventArgs e)
        {
            if (CycleListDataGrid.SelectedItem is not CycleSummaryListItem selected)
            {
                UpdateStatus("cycle を選択してください。");
                return;
            }

            await LoadCycleTraceAsync(selected.CycleId);
        }

        private async void RefreshCurrentStateButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadCurrentStateAsync();
        }

        private async void CycleListDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingCycles || CycleListDataGrid.SelectedItem is not CycleSummaryListItem selected)
            {
                return;
            }

            await LoadCycleTraceAsync(selected.CycleId);
        }

        private async Task LoadCycleSummariesAsync(bool preserveSelection)
        {
            if (_isLoadingCycles)
            {
                return;
            }

            _isLoadingCycles = true;
            RefreshButton.IsEnabled = false;
            ReloadTraceButton.IsEnabled = false;

            try
            {
                var selectedCycleId = preserveSelection && CycleListDataGrid.SelectedItem is CycleSummaryListItem current
                    ? current.CycleId
                    : null;
                var client = EnsureApiClient();
                if (client == null)
                {
                    ClearTraceView("OtomeKairo の token または base URL が未設定です。");
                    return;
                }

                UpdateStatus("cycle 一覧を読み込み中...");
                var response = await client.GetCycleSummariesAsync(limit: 80).ConfigureAwait(true);
                var items = response.CycleSummaries
                    .Select(summary => new CycleSummaryListItem(summary))
                    .ToList();

                CycleListDataGrid.ItemsSource = items;
                UpdateStatus($"cycle 一覧を読み込みました。件数={items.Count}");

                if (items.Count == 0)
                {
                    ClearTraceView("cycle がありません。");
                    return;
                }

                var nextSelection = items.FirstOrDefault(item => item.CycleId == selectedCycleId) ?? items[0];
                CycleListDataGrid.SelectedItem = nextSelection;
                CycleListDataGrid.ScrollIntoView(nextSelection);
                await LoadCycleTraceAsync(nextSelection.CycleId);
            }
            catch (Exception ex)
            {
                UpdateStatus($"cycle 一覧の読み込みに失敗しました: {ex.Message}");
                ClearTraceView("cycle 一覧の読み込みに失敗しました。");
            }
            finally
            {
                _isLoadingCycles = false;
                RefreshButton.IsEnabled = true;
                ReloadTraceButton.IsEnabled = true;
            }
        }

        private async Task LoadCycleTraceAsync(string cycleId)
        {
            if (_isLoadingTrace)
            {
                return;
            }

            _isLoadingTrace = true;
            ReloadTraceButton.IsEnabled = false;

            try
            {
                var client = EnsureApiClient();
                if (client == null)
                {
                    ClearTraceView("OtomeKairo の token または base URL が未設定です。");
                    return;
                }

                _traceLoadCts?.Cancel();
                _traceLoadCts?.Dispose();
                _traceLoadCts = new CancellationTokenSource();

                UpdateStatus($"cycle trace を読み込み中... {cycleId}");
                var trace = await client.GetCycleTraceAsync(cycleId, _traceLoadCts.Token).ConfigureAwait(true);

                var cycleSummary = trace.CycleSummary;
                SelectedCycleTitleTextBlock.Text = cycleId;
                SelectedCycleMetaTextBlock.Text = BuildCycleMetaText(cycleSummary);

                var factTrace = TryGetProperty(trace.RecallTrace, "fact_resolution_trace");
                OverviewTextBox.Text = BuildOverview(trace, factTrace);
                FactResolutionTraceTextBox.Text = PrettyJson(factTrace);
                RecallTraceTextBox.Text = PrettyJson(trace.RecallTrace);
                CycleTraceTextBox.Text = JsonSerializer.Serialize(trace, _jsonSerializerOptions);

                UpdateStatus($"cycle trace を表示中: {cycleId}");
            }
            catch (OperationCanceledException)
            {
                UpdateStatus("cycle trace の読み込みをキャンセルしました。");
            }
            catch (Exception ex)
            {
                UpdateStatus($"cycle trace の読み込みに失敗しました: {ex.Message}");
                ClearTraceView("cycle trace の読み込みに失敗しました。");
            }
            finally
            {
                _isLoadingTrace = false;
                ReloadTraceButton.IsEnabled = true;
            }
        }

        private async Task LoadCurrentStateAsync()
        {
            if (_isLoadingCurrentState)
            {
                return;
            }

            _isLoadingCurrentState = true;
            RefreshCurrentStateButton.IsEnabled = false;

            try
            {
                var client = EnsureApiClient();
                if (client == null)
                {
                    ClearCurrentStateView("OtomeKairo の token または base URL が未設定です。");
                    return;
                }

                _currentStateLoadCts?.Cancel();
                _currentStateLoadCts?.Dispose();
                _currentStateLoadCts = new CancellationTokenSource();

                UpdateStatus("現在状態 snapshot を読み込み中...");
                var snapshot = await client.GetCurrentStateInspectionAsync(_currentStateLoadCts.Token).ConfigureAwait(true);

                CurrentStateOverviewTextBox.Text = BuildCurrentStateOverview(snapshot);
                CurrentStateSnapshotTextBox.Text = PrettyJson(snapshot.CurrentState);
                CurrentRuntimeTextBox.Text = JsonSerializer.Serialize(
                    new
                    {
                        generated_at = snapshot.GeneratedAt,
                        settings_snapshot = snapshot.SettingsSnapshot,
                        runtime_summary = snapshot.RuntimeSummary,
                        runtime_detail = snapshot.RuntimeDetail,
                    },
                    _jsonSerializerOptions);
                CurrentCapabilityTextBox.Text = PrettyJson(snapshot.CapabilityInspection);

                UpdateStatus($"現在状態 snapshot を表示中: {snapshot.GeneratedAt}");
            }
            catch (OperationCanceledException)
            {
                UpdateStatus("現在状態 snapshot の読み込みをキャンセルしました。");
            }
            catch (Exception ex)
            {
                UpdateStatus($"現在状態 snapshot の読み込みに失敗しました: {ex.Message}");
                ClearCurrentStateView("現在状態 snapshot の読み込みに失敗しました。");
            }
            finally
            {
                _isLoadingCurrentState = false;
                RefreshCurrentStateButton.IsEnabled = true;
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

        private void ClearTraceView(string message)
        {
            SelectedCycleTitleTextBlock.Text = "cycle 未選択";
            SelectedCycleMetaTextBlock.Text = string.Empty;
            OverviewTextBox.Text = message;
            FactResolutionTraceTextBox.Text = message;
            RecallTraceTextBox.Text = string.Empty;
            CycleTraceTextBox.Text = string.Empty;
        }

        private void ClearCurrentStateView(string message)
        {
            CurrentStateOverviewTextBox.Text = message;
            CurrentStateSnapshotTextBox.Text = string.Empty;
            CurrentRuntimeTextBox.Text = string.Empty;
            CurrentCapabilityTextBox.Text = string.Empty;
        }

        private void UpdateStatus(string message)
        {
            StatusTextBlock.Text = message;
        }

        private string BuildCycleMetaText(JsonElement cycleSummary)
        {
            var startedAt = GetString(cycleSummary, "started_at");
            var triggerKind = GetString(cycleSummary, "trigger_kind");
            var resultKind = GetString(cycleSummary, "result_kind");
            var failed = GetString(cycleSummary, "failed");
            return $"started_at={startedAt} / trigger={triggerKind} / result={resultKind} / failed={failed}";
        }

        private string BuildOverview(OtomeKairoCycleTrace trace, JsonElement factTrace)
        {
            var builder = new StringBuilder();
            builder.AppendLine($"cycle_id: {trace.CycleId}");
            builder.AppendLine($"started_at: {GetString(trace.CycleSummary, "started_at")}");
            builder.AppendLine($"trigger_kind: {GetString(trace.CycleSummary, "trigger_kind")}");
            builder.AppendLine($"result_kind: {GetString(trace.CycleSummary, "result_kind")}");
            builder.AppendLine();

            if (factTrace.ValueKind != JsonValueKind.Object)
            {
                builder.AppendLine("fact_resolution_trace: (なし)");
                return builder.ToString();
            }

            var query = TryGetProperty(factTrace, "query");
            builder.AppendLine($"resolver_path: {GetString(factTrace, "resolver_path")}");
            builder.AppendLine($"result_status: {GetString(factTrace, "result_status")}");
            builder.AppendLine($"contract: {GetString(query, "contract")}");
            builder.AppendLine($"boundary: {GetString(query, "boundary")}");
            builder.AppendLine($"target_actor: {GetString(query, "target_actor")}");
            builder.AppendLine($"reason_codes: {JoinArrayValues(query, "reason_codes")}");
            builder.AppendLine($"query_terms: {JoinArrayValues(query, "query_terms")}");
            builder.AppendLine($"missing_reason: {GetString(factTrace, "missing_reason")}");
            builder.AppendLine($"reply_guidance: {GetString(factTrace, "reply_guidance")}");
            builder.AppendLine($"input_text: {GetString(query, "input_text")}");
            builder.AppendLine();

            AppendArraySection(
                builder,
                "boundary_event_candidates",
                TryGetProperty(factTrace, "boundary_event_candidates"),
                element => $"{GetString(element, "recorded_date")} {GetString(element, "event_id")} {GetString(element, "text")}"
            );
            AppendArraySection(
                builder,
                "cycle_event_candidates",
                TryGetProperty(factTrace, "cycle_event_candidates"),
                element => $"{GetString(element, "recorded_date")} {GetString(element, "event_id")} {GetString(element, "text")}"
            );
            AppendArraySection(
                builder,
                "statement_event_candidates",
                TryGetProperty(factTrace, "statement_event_candidates"),
                element => $"{GetString(element, "recorded_date")} {GetString(element, "event_id")} {GetString(element, "text")}"
            );
            AppendArraySection(
                builder,
                "conflict_candidates",
                TryGetProperty(factTrace, "conflict_candidates"),
                element => $"{GetString(element, "source_id")} {GetString(element, "text")}"
            );
            AppendArraySection(
                builder,
                "adopted_evidence_items",
                TryGetProperty(factTrace, "adopted_evidence_items"),
                element => $"{GetString(element, "recorded_date")} {GetString(element, "event_id")} {GetString(element, "source_id")} {GetString(element, "text")}"
            );
            AppendConsistencyChecks(builder, TryGetProperty(factTrace, "consistency_checks"));
            AppendSelectedRecallSections(builder, TryGetProperty(factTrace, "selected_recall_sections"));

            return builder.ToString();
        }

        private string BuildCurrentStateOverview(OtomeKairoCurrentStateSnapshot snapshot)
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

        private void AppendConsistencyChecks(StringBuilder builder, JsonElement checks)
        {
            builder.AppendLine("consistency_checks:");
            if (checks.ValueKind != JsonValueKind.Array || checks.GetArrayLength() == 0)
            {
                builder.AppendLine("  (none)");
                builder.AppendLine();
                return;
            }

            foreach (var check in checks.EnumerateArray())
            {
                builder.AppendLine(
                    $"  - {GetString(check, "check_type")} status={GetString(check, "status")} canonical={GetString(check, "canonical_recorded_date")}"
                );
                var claims = TryGetProperty(check, "claims");
                if (claims.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var claim in claims.EnumerateArray())
                {
                    builder.AppendLine(
                        $"      * {GetString(claim, "section")} {GetString(claim, "source_id")} {GetString(claim, "claim_kind")}={GetString(claim, "claim_value")} {GetString(claim, "summary_text")}"
                    );
                }
            }
            builder.AppendLine();
        }

        private void AppendSelectedRecallSections(StringBuilder builder, JsonElement sections)
        {
            builder.AppendLine("selected_recall_sections:");
            if (sections.ValueKind != JsonValueKind.Object)
            {
                builder.AppendLine("  (none)");
                return;
            }

            foreach (var property in sections.EnumerateObject())
            {
                builder.AppendLine($"  {property.Name}:");
                if (property.Value.ValueKind != JsonValueKind.Array || property.Value.GetArrayLength() == 0)
                {
                    builder.AppendLine("    (none)");
                    continue;
                }

                foreach (var item in property.Value.EnumerateArray())
                {
                    builder.AppendLine($"    - {BuildRecallSectionLine(item)}");
                }
            }
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

        private string BuildRecallSectionLine(JsonElement item)
        {
            var primaryId = FirstNonEmpty(
                GetString(item, "memory_unit_id"),
                GetString(item, "episode_id"),
                GetString(item, "event_id"),
                GetString(item, "source_id")
            );
            var summary = FirstNonEmpty(
                GetString(item, "summary_text"),
                GetString(item, "text"),
                GetString(item, "summary")
            );
            var predicate = GetString(item, "predicate");
            var objectValue = GetString(item, "object_ref_or_value");
            var formedAt = GetString(item, "formed_at");
            var recordedDate = GetString(item, "recorded_date");

            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(primaryId))
            {
                parts.Add(primaryId);
            }
            if (!string.IsNullOrWhiteSpace(predicate))
            {
                parts.Add($"predicate={predicate}");
            }
            if (!string.IsNullOrWhiteSpace(objectValue))
            {
                parts.Add($"value={objectValue}");
            }
            if (!string.IsNullOrWhiteSpace(formedAt))
            {
                parts.Add($"formed_at={formedAt}");
            }
            if (!string.IsNullOrWhiteSpace(recordedDate))
            {
                parts.Add($"recorded_date={recordedDate}");
            }
            if (!string.IsNullOrWhiteSpace(summary))
            {
                parts.Add(summary);
            }
            return string.Join(" / ", parts);
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

        private static string JoinArrayValues(JsonElement element, string propertyName)
        {
            var values = TryGetProperty(element, propertyName);
            if (values.ValueKind != JsonValueKind.Array)
            {
                return string.Empty;
            }

            return string.Join(", ", values.EnumerateArray().Select(GetElementString));
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

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return string.Empty;
        }

        private sealed class CycleSummaryListItem
        {
            public CycleSummaryListItem(OtomeKairoCycleSummary summary)
            {
                CycleId = summary.CycleId;
                TriggerKind = summary.TriggerKind;
                ResultKind = summary.ResultKind;
                Failed = summary.Failed;
                StartedAtDisplay = TryFormatTimestamp(summary.StartedAt);
            }

            public string CycleId { get; }

            public string StartedAtDisplay { get; }

            public string TriggerKind { get; }

            public string ResultKind { get; }

            public bool Failed { get; }

            private static string TryFormatTimestamp(string value)
            {
                if (DateTimeOffset.TryParse(value, out var parsed))
                {
                    return parsed.ToString("MM/dd HH:mm:ss");
                }

                return value;
            }
        }
    }
}
