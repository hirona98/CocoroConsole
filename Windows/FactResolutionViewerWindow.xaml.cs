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
        private bool _isLoadingCycles;
        private bool _isLoadingTrace;

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
        }

        private void FactResolutionViewerWindow_Closed(object? sender, EventArgs e)
        {
            IsClosed = true;
            _traceLoadCts?.Cancel();
            _traceLoadCts?.Dispose();
            _traceLoadCts = null;
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
            return $"開始時刻={startedAt} / トリガー={triggerKind} / 結果={resultKind} / 失敗={failed}";
        }

        private string BuildOverview(OtomeKairoCycleTrace trace, JsonElement factTrace)
        {
            var builder = new StringBuilder();
            builder.AppendLine($"サイクルID: {trace.CycleId}");
            builder.AppendLine($"開始時刻: {GetString(trace.CycleSummary, "started_at")}");
            builder.AppendLine($"トリガー種別: {GetString(trace.CycleSummary, "trigger_kind")}");
            builder.AppendLine($"結果種別: {GetString(trace.CycleSummary, "result_kind")}");
            builder.AppendLine();

            if (factTrace.ValueKind != JsonValueKind.Object)
            {
                builder.AppendLine("根拠解決トレース: （なし）");
                return builder.ToString();
            }

            var query = TryGetProperty(factTrace, "query");
            builder.AppendLine($"解決経路: {GetString(factTrace, "resolver_path")}");
            builder.AppendLine($"結果状態: {GetString(factTrace, "result_status")}");
            builder.AppendLine($"契約: {GetString(query, "contract")}");
            builder.AppendLine($"境界: {GetString(query, "boundary")}");
            builder.AppendLine($"対象話者: {GetString(query, "target_actor")}");
            builder.AppendLine($"理由コード: {JoinArrayValues(query, "reason_codes")}");
            builder.AppendLine($"クエリ語: {JoinArrayValues(query, "query_terms")}");
            builder.AppendLine($"未解決理由: {GetString(factTrace, "missing_reason")}");
            builder.AppendLine($"返信ガイダンス: {GetString(factTrace, "reply_guidance")}");
            builder.AppendLine($"入力文: {GetString(query, "input_text")}");
            builder.AppendLine();

            AppendArraySection(
                builder,
                "境界候補イベント",
                TryGetProperty(factTrace, "boundary_event_candidates"),
                element => $"{GetString(element, "recorded_date")} {GetString(element, "event_id")} {GetString(element, "text")}"
            );
            AppendArraySection(
                builder,
                "サイクル候補イベント",
                TryGetProperty(factTrace, "cycle_event_candidates"),
                element => $"{GetString(element, "recorded_date")} {GetString(element, "event_id")} {GetString(element, "text")}"
            );
            AppendArraySection(
                builder,
                "発話候補イベント",
                TryGetProperty(factTrace, "statement_event_candidates"),
                element => $"{GetString(element, "recorded_date")} {GetString(element, "event_id")} {GetString(element, "text")}"
            );
            AppendArraySection(
                builder,
                "矛盾候補",
                TryGetProperty(factTrace, "conflict_candidates"),
                element => $"{GetString(element, "source_id")} {GetString(element, "text")}"
            );
            AppendArraySection(
                builder,
                "採用根拠",
                TryGetProperty(factTrace, "adopted_evidence_items"),
                element => $"{GetString(element, "recorded_date")} {GetString(element, "event_id")} {GetString(element, "source_id")} {GetString(element, "text")}"
            );
            AppendConsistencyChecks(builder, TryGetProperty(factTrace, "consistency_checks"));
            AppendSelectedRecallSections(builder, TryGetProperty(factTrace, "selected_recall_sections"));

            return builder.ToString();
        }

        private void AppendConsistencyChecks(StringBuilder builder, JsonElement checks)
        {
            builder.AppendLine("整合性チェック:");
            if (checks.ValueKind != JsonValueKind.Array || checks.GetArrayLength() == 0)
            {
                builder.AppendLine("  （なし）");
                builder.AppendLine();
                return;
            }

            foreach (var check in checks.EnumerateArray())
            {
                builder.AppendLine(
                    $"  - 種別={GetString(check, "check_type")} 状態={GetString(check, "status")} 正準日付={GetString(check, "canonical_recorded_date")}"
                );
                var claims = TryGetProperty(check, "claims");
                if (claims.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var claim in claims.EnumerateArray())
                {
                    builder.AppendLine(
                        $"      * セクション={GetString(claim, "section")} ソースID={GetString(claim, "source_id")} {GetString(claim, "claim_kind")}={GetString(claim, "claim_value")} {GetString(claim, "summary_text")}"
                    );
                }
            }
            builder.AppendLine();
        }

        private void AppendSelectedRecallSections(StringBuilder builder, JsonElement sections)
        {
            builder.AppendLine("選択された参照セクション:");
            if (sections.ValueKind != JsonValueKind.Object)
            {
                builder.AppendLine("  （なし）");
                return;
            }

            foreach (var property in sections.EnumerateObject())
            {
                builder.AppendLine($"  {property.Name}:");
                if (property.Value.ValueKind != JsonValueKind.Array || property.Value.GetArrayLength() == 0)
                {
                    builder.AppendLine("    （なし）");
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
                parts.Add($"述語={predicate}");
            }
            if (!string.IsNullOrWhiteSpace(objectValue))
            {
                parts.Add($"値={objectValue}");
            }
            if (!string.IsNullOrWhiteSpace(formedAt))
            {
                parts.Add($"形成時刻={formedAt}");
            }
            if (!string.IsNullOrWhiteSpace(recordedDate))
            {
                parts.Add($"記録日={recordedDate}");
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
