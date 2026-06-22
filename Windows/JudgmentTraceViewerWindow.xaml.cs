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
    /// 判断サイクルの要点と詳細 trace を確認する inspection ビューアー。
    /// </summary>
    public partial class JudgmentTraceViewerWindow : Window
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

        public JudgmentTraceViewerWindow()
        {
            InitializeComponent();
            Loaded += JudgmentTraceViewerWindow_Loaded;
            Closed += JudgmentTraceViewerWindow_Closed;
        }

        private async void JudgmentTraceViewerWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadCycleSummariesAsync(preserveSelection: false);
        }

        private void JudgmentTraceViewerWindow_Closed(object? sender, EventArgs e)
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
                var traceTask = client.GetCycleTraceAsync(cycleId, _traceLoadCts.Token);
                var cognitiveContextTask = client.GetCycleCognitiveContextAsync(cycleId, _traceLoadCts.Token);
                await Task.WhenAll(traceTask, cognitiveContextTask).ConfigureAwait(true);
                var trace = await traceTask.ConfigureAwait(true);
                var cognitiveContext = await cognitiveContextTask.ConfigureAwait(true);

                var cycleSummary = trace.CycleSummary;
                SelectedCycleTitleTextBlock.Text = cycleId;
                SelectedCycleMetaTextBlock.Text = BuildCycleMetaText(cycleSummary);

                var evidenceResolutionTrace = TryGetProperty(trace.RecallTrace, "fact_resolution_trace");
                UpdateOverview(trace, cognitiveContext, evidenceResolutionTrace);
                EvidenceResolutionTraceTextBox.Text = PrettyJson(evidenceResolutionTrace);
                RecallTraceTextBox.Text = PrettyJson(trace.RecallTrace);
                ActivityTraceTextBox.Text = PrettyJson(trace.ActivityTrace);
                CognitiveContextTextBox.Text = PrettyJson(cognitiveContext);
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
            SetOverviewMessage(message);
            EvidenceResolutionTraceTextBox.Text = message;
            RecallTraceTextBox.Text = string.Empty;
            ActivityTraceTextBox.Text = string.Empty;
            CognitiveContextTextBox.Text = string.Empty;
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
            return $"開始時刻: {startedAt} / きっかけ: {DescribeTriggerKind(triggerKind)} / 結果: {DescribeResultKind(resultKind)} / 失敗: {DescribeBool(failed)}";
        }

        private void UpdateOverview(
            OtomeKairoCycleTrace trace,
            OtomeKairoCycleCognitiveContext cognitiveContext,
            JsonElement evidenceResolutionTrace)
        {
            var compact = TryGetProperty(trace.ResultTrace, "trigger_compact_summary");
            var entrySummary = TryGetProperty(compact, "entry_summary");
            var decisionSummary = TryGetProperty(compact, "decision_summary");
            var resultSummary = TryGetProperty(compact, "result_summary");

            var builder = new StringBuilder();
            builder.AppendLine("この判断で起きたこと");
            builder.AppendLine($"  開始: {GetString(trace.CycleSummary, "started_at")}");
            builder.AppendLine($"  きっかけ: {DescribeTriggerKind(GetString(trace.CycleSummary, "trigger_kind"))}");
            builder.AppendLine($"  結果: {DescribeResultKind(GetString(trace.CycleSummary, "result_kind"))}");
            AppendLineIfPresent(builder, "  発話", FirstNonEmpty(GetString(trace.ResultTrace, "speech_summary"), GetString(resultSummary, "speech_summary")));
            AppendLineIfPresent(builder, "  見送り理由", FirstNonEmpty(GetString(trace.ResultTrace, "noop_reason_summary"), GetString(resultSummary, "noop_reason_summary")));
            AppendLineIfPresent(builder, "  失敗理由", FirstNonEmpty(GetString(trace.ResultTrace, "internal_failure_summary"), GetString(resultSummary, "internal_failure_summary")));
            JudgmentSummaryTextBlock.Text = CleanOverviewText(builder);

            builder.Clear();
            builder.AppendLine("入力と状況");
            AppendLineIfPresent(builder, "  入力", FirstNonEmpty(
                GetString(TryGetProperty(compact, "current_input_summary"), "text"),
                GetString(entrySummary, "input_summary"),
                GetString(trace.InputTrace, "input_summary"),
                GetString(trace.DecisionTrace, "current_context_summary")
            ));
            AppendLineIfPresent(builder, "  観測", BuildObservationLine(TryGetProperty(entrySummary, "observation_summary")));
            AppendLineIfPresent(builder, "  追加文脈", FirstNonEmpty(
                GetString(trace.InputTrace, "input_context_addition_summary"),
                GetString(trace.DecisionTrace, "input_context_addition_summary")
            ));
            AppendLineIfPresent(builder, "  世界状態", BuildWorldStateLine(trace.WorldStateTrace));
            AppendLineIfPresent(builder, "  継続中の行動", BuildReadableObjectLine(FirstObject(
                TryGetProperty(trace.InputTrace, "ongoing_action_summary"),
                TryGetProperty(trace.DecisionTrace, "ongoing_action_summary")
            )));
            InputContextOverviewTextBlock.Text = CleanOverviewText(builder);

            builder.Clear();
            builder.AppendLine("判断理由");
            AppendLineIfPresent(builder, "  判断", DescribeResultKind(FirstNonEmpty(
                GetString(trace.DecisionTrace, "result_kind"),
                GetString(decisionSummary, "kind")
            )));
            AppendLineIfPresent(builder, "  理由", FirstNonEmpty(
                GetString(trace.DecisionTrace, "reason_summary"),
                GetString(decisionSummary, "reason_summary")
            ));
            AppendLineIfPresent(builder, "  前景化", BuildForegroundSelectionLine(cognitiveContext.ForegroundSelection));
            AppendLineIfPresent(builder, "  候補盤面", BuildWorkspaceContextLine(cognitiveContext.WorkspaceContextSummary));
            AppendLineIfPresent(builder, "  人格設定", GetString(trace.DecisionTrace, "persona_summary"));
            AppendLineIfPresent(builder, "  記憶集合", GetString(trace.DecisionTrace, "memory_summary"));
            AppendLineIfPresent(builder, "  能力要求", BuildCapabilityLine(FirstObject(
                TryGetProperty(trace.ResultTrace, "capability_dispatch_summary"),
                TryGetProperty(trace.ResultTrace, "capability_request_summary"),
                TryGetProperty(trace.DecisionTrace, "capability_request_candidate_summary")
            )));
            DecisionReasonOverviewTextBlock.Text = CleanOverviewText(builder);

            builder.Clear();
            AppendEvidenceResolutionOverview(builder, evidenceResolutionTrace);
            EvidenceOverviewTextBlock.Text = CleanOverviewText(builder);

            builder.Clear();
            AppendRecallOverview(builder, trace.RecallTrace);
            RecallOverviewTextBlock.Text = CleanOverviewText(builder);

            builder.Clear();
            AppendCognitiveContextOverview(builder, cognitiveContext);
            SetCognitiveContextOverview(builder);

            builder.Clear();
            AppendWorldStateOverview(builder, trace.WorldStateTrace);
            WorldStateOverviewTextBlock.Text = CleanOverviewText(builder);

            builder.Clear();
            AppendActivityOverview(builder, trace.ActivityTrace);
            ActivityOverviewTextBlock.Text = CleanOverviewText(builder);

            builder.Clear();
            AppendMemoryOverview(builder, trace.MemoryTrace);
            MemoryOverviewTextBlock.Text = CleanOverviewText(builder);
        }

        private void SetOverviewMessage(string message)
        {
            JudgmentSummaryTextBlock.Text = message;
            InputContextOverviewTextBlock.Text = string.Empty;
            DecisionReasonOverviewTextBlock.Text = string.Empty;
            EvidenceOverviewTextBlock.Text = string.Empty;
            RecallOverviewTextBlock.Text = string.Empty;
            CognitiveContextOverviewPanel.Children.Clear();
            WorldStateOverviewTextBlock.Text = string.Empty;
            ActivityOverviewTextBlock.Text = string.Empty;
            MemoryOverviewTextBlock.Text = string.Empty;
        }

        private static string CleanOverviewText(StringBuilder builder)
        {
            var lines = builder
                .ToString()
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                .Skip(1)
                .Select(line => line.TrimStart())
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToList();

            return lines.Count == 0 ? "（なし）" : string.Join(Environment.NewLine, lines);
        }

        private void SetCognitiveContextOverview(StringBuilder builder)
        {
            CognitiveContextOverviewPanel.Children.Clear();

            var lines = builder
                .ToString()
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                .Skip(1)
                .Select(line => line.TrimStart())
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToList();

            if (lines.Count == 0)
            {
                CognitiveContextOverviewPanel.Children.Add(CreateOverviewTextBlock("（なし）"));
                return;
            }

            foreach (var line in lines)
            {
                if (line.StartsWith("- ", StringComparison.Ordinal))
                {
                    AddBulletOverviewLine(line.Substring(2).TrimStart());
                }
                else
                {
                    CognitiveContextOverviewPanel.Children.Add(CreateOverviewTextBlock(line));
                }
            }
        }

        private void AddBulletOverviewLine(string text)
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
            CognitiveContextOverviewPanel.Children.Add(grid);
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

        private void AppendEvidenceResolutionOverview(StringBuilder builder, JsonElement evidenceResolutionTrace)
        {
            builder.AppendLine("根拠の解決");

            if (evidenceResolutionTrace.ValueKind != JsonValueKind.Object)
            {
                builder.AppendLine("  この判断では個別根拠の解決はありません。");
                builder.AppendLine();
                return;
            }

            var query = TryGetProperty(evidenceResolutionTrace, "query");
            AppendLineIfPresent(builder, "  状態", DescribeStatus(GetString(evidenceResolutionTrace, "result_status")));
            AppendLineIfPresent(builder, "  探したこと", FirstNonEmpty(JoinArrayValues(query, "query_terms"), GetString(query, "input_text")));
            AppendLineIfPresent(builder, "  対象", BuildTargetLine(query));
            AppendLineIfPresent(builder, "  未解決理由", GetString(evidenceResolutionTrace, "missing_reason"));
            AppendLineIfPresent(builder, "  発話方針", GetString(evidenceResolutionTrace, "speech_guidance"));
            AppendTopArraySection(
                builder,
                "  採用した根拠",
                TryGetProperty(evidenceResolutionTrace, "adopted_evidence_items"),
                3,
                BuildEventLine
            );
            AppendTopArraySection(builder, "  確認した候補", TryGetProperty(evidenceResolutionTrace, "boundary_event_candidates"), 2, BuildEventLine);
            AppendTopArraySection(builder, "  近いサイクル候補", TryGetProperty(evidenceResolutionTrace, "cycle_event_candidates"), 2, BuildEventLine);
            AppendTopArraySection(builder, "  近い発話候補", TryGetProperty(evidenceResolutionTrace, "statement_event_candidates"), 2, BuildEventLine);
            AppendTopArraySection(builder, "  矛盾候補", TryGetProperty(evidenceResolutionTrace, "conflict_candidates"), 2, BuildEventLine);
            AppendConsistencyOverview(builder, TryGetProperty(evidenceResolutionTrace, "consistency_checks"));
            builder.AppendLine();
        }

        private void AppendRecallOverview(StringBuilder builder, JsonElement recallTrace)
        {
            builder.AppendLine("想起");
            AppendLineIfPresent(builder, "  候補数", GetString(recallTrace, "candidate_count"));
            AppendLineIfPresent(builder, "  採用した記憶", BuildSelectedIdsLine(recallTrace));
            AppendLineIfPresent(builder, "  採用理由", GetString(recallTrace, "adopted_reason_summary"));
            AppendLineIfPresent(builder, "  落とした候補", GetString(recallTrace, "rejected_candidate_summary"));
            AppendLineIfPresent(builder, "  要約", BuildReadableObjectLine(TryGetProperty(recallTrace, "recall_pack_summary")));
            builder.AppendLine();
        }

        private void AppendCognitiveContextOverview(StringBuilder builder, OtomeKairoCycleCognitiveContext cognitiveContext)
        {
            builder.AppendLine("前景化と派生 view");
            AppendLineIfPresent(builder, "  前景化", BuildForegroundSelectionLine(cognitiveContext.ForegroundSelection));
            AppendLineIfPresent(builder, "  候補盤面", BuildWorkspaceContextLine(cognitiveContext.WorkspaceContextSummary));
            AppendLineIfPresent(builder, "  自己状態", BuildSelfStateLine(cognitiveContext.SelfStateContext));
            AppendLineIfPresent(builder, "  関係 view", BuildContextCountLine(cognitiveContext.RelationshipContext, "relationship_items", "関係"));
            AppendLineIfPresent(builder, "  予測差分", BuildContextCountLine(cognitiveContext.PredictionErrorContext, "signals", "差分"));
            AppendLineIfPresent(builder, "  静かな再浮上", BuildContextCountLine(cognitiveContext.DefaultModeContext, "resurfacing_candidates", "再浮上"));
            AppendTopArraySection(
                builder,
                "  workspace 候補",
                TryGetProperty(cognitiveContext.WorkspaceContextSummary, "workspace_candidates"),
                5,
                BuildWorkspaceCandidateLine);
            AppendTopArraySection(
                builder,
                "  抑制した候補",
                TryGetProperty(cognitiveContext.ForegroundSelection, "suppressed_factors"),
                3,
                BuildSuppressedFactorLine);
            AppendTopArraySection(
                builder,
                "  感覚信頼度",
                TryGetProperty(cognitiveContext.SelfStateContext, "sensory_confidence"),
                3,
                BuildContextItemLine);
            AppendTopArraySection(
                builder,
                "  agency 信頼度",
                TryGetProperty(cognitiveContext.SelfStateContext, "agency_confidence"),
                3,
                BuildContextItemLine);
            AppendTopArraySection(
                builder,
                "  関係項目",
                TryGetProperty(cognitiveContext.RelationshipContext, "relationship_items"),
                4,
                BuildContextItemLine);
            AppendTopArraySection(
                builder,
                "  関係感情",
                TryGetProperty(cognitiveContext.RelationshipContext, "affect_items"),
                3,
                BuildContextItemLine);
            AppendTopArraySection(
                builder,
                "  予測差分候補",
                TryGetProperty(cognitiveContext.PredictionErrorContext, "signals"),
                4,
                BuildContextItemLine);
            AppendTopArraySection(
                builder,
                "  静かな再浮上候補",
                TryGetProperty(cognitiveContext.DefaultModeContext, "resurfacing_candidates"),
                4,
                BuildContextItemLine);
            builder.AppendLine();
        }

        private void AppendWorldStateOverview(StringBuilder builder, JsonElement worldStateTrace)
        {
            builder.AppendLine("世界状態の更新");
            AppendLineIfPresent(builder, "  状態", DescribeStatus(GetString(worldStateTrace, "result_status")));
            AppendLineIfPresent(builder, "  更新数", BuildWorldStateCountLine(worldStateTrace));
            AppendLineIfPresent(builder, "  失敗理由", GetString(worldStateTrace, "failure_reason"));
            AppendTopArraySection(builder, "  判断に入った状態", TryGetProperty(worldStateTrace, "foreground_world_state"), 3, BuildWorldStateItemLine);
            builder.AppendLine();
        }

        private void AppendActivityOverview(StringBuilder builder, JsonElement activityTrace)
        {
            builder.AppendLine("活動状態の推定");
            AppendLineIfPresent(builder, "  状態", DescribeStatus(GetString(activityTrace, "result_status")));
            AppendLineIfPresent(builder, "  候補数", GetString(activityTrace, "candidate_count"));
            AppendLineIfPresent(builder, "  更新数", BuildActivityCountLine(activityTrace));
            AppendLineIfPresent(builder, "  候補", BuildReadableObjectLine(TryGetProperty(activityTrace, "candidate_summary")));
            AppendLineIfPresent(builder, "  現在活動", BuildActivityContextLine(TryGetProperty(TryGetProperty(activityTrace, "activity_context"), "current_activity")));
            AppendLineIfPresent(builder, "  直前活動", BuildActivityContextLine(TryGetProperty(TryGetProperty(activityTrace, "activity_context"), "previous_activity")));
            AppendLineIfPresent(builder, "  失敗理由", GetString(activityTrace, "failure_reason"));
            builder.AppendLine();
        }

        private void AppendMemoryOverview(StringBuilder builder, JsonElement memoryTrace)
        {
            builder.AppendLine("記憶への反映");
            AppendLineIfPresent(builder, "  保存", BuildReadableObjectLine(TryGetProperty(memoryTrace, "turn_consolidation")));
            AppendLineIfPresent(builder, "  ベクトル同期", BuildReadableObjectLine(TryGetProperty(memoryTrace, "vector_index_sync")));
            AppendLineIfPresent(builder, "  内省整理", BuildReadableObjectLine(TryGetProperty(memoryTrace, "reflective_consolidation")));
            AppendLineIfPresent(builder, "  失敗理由", GetString(memoryTrace, "failure_reason"));
            builder.AppendLine();
        }

        private void AppendTopArraySection(StringBuilder builder, string title, JsonElement array, int maxItems, Func<JsonElement, string> formatter)
        {
            if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() == 0)
            {
                return;
            }

            builder.AppendLine($"{title}:");
            var shownCount = 0;
            foreach (var item in array.EnumerateArray())
            {
                if (shownCount >= maxItems)
                {
                    break;
                }

                var line = formatter(item);
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                builder.AppendLine($"    - {line}");
                shownCount++;
            }

            var remainingCount = array.GetArrayLength() - shownCount;
            if (remainingCount > 0)
            {
                builder.AppendLine($"    他 {remainingCount} 件は詳細タブにあります。");
            }
        }

        private void AppendConsistencyOverview(StringBuilder builder, JsonElement checks)
        {
            if (checks.ValueKind != JsonValueKind.Array || checks.GetArrayLength() == 0)
            {
                return;
            }

            var summaries = checks.EnumerateArray()
                .Select(check => FirstNonEmpty(DescribeStatus(GetString(check, "status")), GetString(check, "check_type")))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Take(3)
                .ToList();
            if (summaries.Count > 0)
            {
                builder.AppendLine($"  整合性: {string.Join(" / ", summaries)}");
            }
        }

        private static void AppendLineIfPresent(StringBuilder builder, string label, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                builder.AppendLine($"{label}: {value.Trim()}");
            }
        }

        private static string BuildEventLine(JsonElement element)
        {
            var text = FirstNonEmpty(
                GetString(element, "text"),
                GetString(element, "summary_text"),
                GetString(element, "claim_value"),
                GetString(element, "source_id")
            );
            var when = FirstNonEmpty(GetString(element, "recorded_date"), GetString(element, "formed_at"));
            var source = FirstNonEmpty(GetString(element, "event_id"), GetString(element, "source_id"));
            return string.Join(" / ", new[] { when, source, text }.Where(value => !string.IsNullOrWhiteSpace(value)));
        }

        private static string BuildWorldStateItemLine(JsonElement element)
        {
            return string.Join(" / ", new[]
            {
                FirstNonEmpty(GetString(element, "state_type"), GetString(element, "kind")),
                FirstNonEmpty(GetString(element, "summary_text"), GetString(element, "summary")),
                FirstNonEmpty(GetString(element, "freshness_hint"), GetString(element, "confidence_hint")),
            }.Where(value => !string.IsNullOrWhiteSpace(value)));
        }

        private static string BuildObservationLine(JsonElement observation)
        {
            if (observation.ValueKind != JsonValueKind.Object)
            {
                return string.Empty;
            }

            return string.Join(" / ", new[]
            {
                FirstNonEmpty(GetString(observation, "summary_text"), GetString(observation, "visual_summary_text"), GetString(observation, "status")),
                GetString(observation, "request_id"),
                GetString(observation, "failure_reason"),
            }.Where(value => !string.IsNullOrWhiteSpace(value)));
        }

        private static string BuildCapabilityLine(JsonElement capability)
        {
            if (capability.ValueKind != JsonValueKind.Object)
            {
                return string.Empty;
            }

            return string.Join(" / ", new[]
            {
                FirstNonEmpty(GetString(capability, "capability_id"), GetString(capability, "capability_kind")),
                BuildReadableObjectLine(TryGetProperty(capability, "request_summary")),
                BuildReadableObjectLine(TryGetProperty(capability, "transition_summary")),
            }.Where(value => !string.IsNullOrWhiteSpace(value)));
        }

        private static string BuildForegroundSelectionLine(JsonElement foregroundSelection)
        {
            if (foregroundSelection.ValueKind != JsonValueKind.Object)
            {
                return string.Empty;
            }

            var suppressedFactors = TryGetProperty(foregroundSelection, "suppressed_factors");
            var suppressedCount = suppressedFactors.ValueKind == JsonValueKind.Array
                ? suppressedFactors.GetArrayLength().ToString()
                : string.Empty;
            return string.Join(" / ", new[]
            {
                GetString(foregroundSelection, "summary_text"),
                LabelValue("主役", GetString(foregroundSelection, "primary_factor_ref")),
                LabelValue("補助", JoinArrayValues(foregroundSelection, "supporting_factor_refs")),
                LabelValue("抑制", suppressedCount),
            }.Where(value => !string.IsNullOrWhiteSpace(value)));
        }

        private static string BuildWorkspaceCandidateLine(JsonElement candidate)
        {
            if (candidate.ValueKind != JsonValueKind.Object)
            {
                return string.Empty;
            }

            return string.Join(" / ", new[]
            {
                LabelValue("ref", GetString(candidate, "factor_ref")),
                FirstNonEmpty(GetString(candidate, "kind"), GetString(candidate, "source")),
                GetString(candidate, "summary_text"),
            }.Where(value => !string.IsNullOrWhiteSpace(value)));
        }

        private static string BuildSuppressedFactorLine(JsonElement factor)
        {
            if (factor.ValueKind != JsonValueKind.Object)
            {
                return string.Empty;
            }

            return string.Join(" / ", new[]
            {
                LabelValue("ref", GetString(factor, "factor_ref")),
                GetString(factor, "reason_summary"),
            }.Where(value => !string.IsNullOrWhiteSpace(value)));
        }

        private static string BuildContextItemLine(JsonElement item)
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                return string.Empty;
            }

            return string.Join(" / ", new[]
            {
                FirstNonEmpty(
                    GetString(item, "summary_text"),
                    GetString(item, "reason_summary"),
                    GetString(item, "detail_summary"),
                    GetString(item, "label")),
                LabelValue("source", GetString(item, "source")),
                LabelValue("ref", FirstNonEmpty(
                    GetString(item, "item_ref"),
                    GetString(item, "candidate_ref"),
                    GetString(item, "factor_ref"),
                    GetString(item, "source_id"))),
                LabelValue("kind", FirstNonEmpty(
                    GetString(item, "signal_kind"),
                    GetString(item, "kind"),
                    GetString(item, "channel"))),
                LabelValue("confidence", FirstNonEmpty(
                    GetString(item, "confidence_hint"),
                    GetString(item, "confidence"))),
            }.Where(value => !string.IsNullOrWhiteSpace(value)));
        }

        private static string BuildWorkspaceContextLine(JsonElement workspaceContextSummary)
        {
            if (workspaceContextSummary.ValueKind != JsonValueKind.Object)
            {
                return string.Empty;
            }

            return string.Join(" / ", new[]
            {
                LabelValue("候補", GetString(workspaceContextSummary, "candidate_count")),
                LabelValue("全候補", GetString(workspaceContextSummary, "total_candidate_count")),
                LabelValue("除外", GetString(workspaceContextSummary, "dropped_candidate_count")),
                BuildReadableObjectLine(TryGetProperty(workspaceContextSummary, "source_counts")),
            }.Where(value => !string.IsNullOrWhiteSpace(value)));
        }

        private static string BuildSelfStateLine(JsonElement selfStateContext)
        {
            if (selfStateContext.ValueKind != JsonValueKind.Object)
            {
                return string.Empty;
            }

            return string.Join(" / ", new[]
            {
                BuildContextCountLine(selfStateContext, "sensory_confidence", "感覚"),
                BuildContextCountLine(selfStateContext, "agency_confidence", "働きかけ"),
                BuildReadableObjectLine(TryGetProperty(selfStateContext, "focus_stability")),
            }.Where(value => !string.IsNullOrWhiteSpace(value)));
        }

        private static string BuildContextCountLine(JsonElement context, string arrayPropertyName, string label)
        {
            var array = TryGetProperty(context, arrayPropertyName);
            if (array.ValueKind != JsonValueKind.Array)
            {
                return string.Empty;
            }

            return $"{label} {array.GetArrayLength()}";
        }

        private static string BuildSelectedIdsLine(JsonElement recallTrace)
        {
            var parts = new List<string>();
            AddCountPart(parts, "記憶", TryGetProperty(recallTrace, "selected_memory_unit_ids"));
            AddCountPart(parts, "エピソード", TryGetProperty(recallTrace, "selected_episode_ids"));
            AddCountPart(parts, "イベント", TryGetProperty(recallTrace, "selected_event_ids"));
            return string.Join(" / ", parts);
        }

        private static void AddCountPart(List<string> parts, string label, JsonElement array)
        {
            if (array.ValueKind == JsonValueKind.Array && array.GetArrayLength() > 0)
            {
                parts.Add($"{label} {array.GetArrayLength()} 件");
            }
        }

        private static string BuildWorldStateLine(JsonElement worldStateTrace)
        {
            var count = GetString(worldStateTrace, "input_world_state_count");
            var status = DescribeStatus(GetString(worldStateTrace, "result_status"));
            if (string.IsNullOrWhiteSpace(count) && string.IsNullOrWhiteSpace(status))
            {
                return string.Empty;
            }

            var countText = string.IsNullOrWhiteSpace(count) ? string.Empty : $"{count} 件";
            return string.Join(" / ", new[] { countText, status }.Where(value => !string.IsNullOrWhiteSpace(value)));
        }

        private static string BuildWorldStateCountLine(JsonElement worldStateTrace)
        {
            var parts = new[]
            {
                CountPart("候補", GetString(worldStateTrace, "candidate_state_count")),
                CountPart("更新", GetString(worldStateTrace, "updated_state_count")),
                CountPart("置換", GetString(worldStateTrace, "replaced_state_count")),
                CountPart("期限切れ", GetString(worldStateTrace, "expired_state_count")),
                CountPart("除外", GetString(worldStateTrace, "dropped_state_count")),
            }.Where(value => !string.IsNullOrWhiteSpace(value));
            return string.Join(" / ", parts);
        }

        private static string BuildActivityCountLine(JsonElement activityTrace)
        {
            var parts = new[]
            {
                CountPart("更新", GetString(activityTrace, "updated_count")),
                CountPart("期限切れ", GetString(activityTrace, "expired_count")),
            }.Where(value => !string.IsNullOrWhiteSpace(value));
            return string.Join(" / ", parts);
        }

        private static string BuildActivityContextLine(JsonElement activity)
        {
            if (activity.ValueKind != JsonValueKind.Object)
            {
                return string.Empty;
            }

            return string.Join(" / ", new[]
            {
                GetString(activity, "label"),
                GetString(activity, "target"),
                DescribeStatus(GetString(activity, "status")),
                GetString(activity, "age_label"),
                GetString(activity, "ended_age_label"),
                GetString(activity, "reason_summary"),
            }.Where(value => !string.IsNullOrWhiteSpace(value)));
        }

        private static string CountPart(string label, string count)
        {
            return string.IsNullOrWhiteSpace(count) ? string.Empty : $"{label} {count}";
        }

        private static string LabelValue(string label, string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : $"{label}={value}";
        }

        private static string BuildTargetLine(JsonElement query)
        {
            return string.Join(" / ", new[]
            {
                GetString(query, "target_actor"),
                GetString(query, "boundary"),
                GetString(query, "contract"),
            }.Where(value => !string.IsNullOrWhiteSpace(value)));
        }

        private static JsonElement FirstObject(params JsonElement[] elements)
        {
            foreach (var element in elements)
            {
                if (element.ValueKind == JsonValueKind.Object)
                {
                    return element;
                }
            }

            return default;
        }

        private static string BuildReadableObjectLine(JsonElement element)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                return string.Empty;
            }

            var preferredKeys = new[]
            {
                "result_status",
                "status",
                "kind",
                "summary_text",
                "summary",
                "reason_summary",
                "detail_summary",
                "transition_source",
                "transition",
                "label",
                "target",
                "reason_code",
                "event_count",
                "episode_id",
                "request_id",
            };
            var parts = new List<string>();
            foreach (var key in preferredKeys)
            {
                var value = GetString(element, key);
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                parts.Add(key.EndsWith("_status", StringComparison.Ordinal) || key == "status"
                    ? DescribeStatus(value)
                    : value);
            }

            return string.Join(" / ", parts.Distinct().Take(4));
        }

        private string PrettyJson(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Undefined)
            {
                return "{}";
            }

            return JsonSerializer.Serialize(element, _jsonSerializerOptions);
        }

        private string PrettyJson<T>(T value)
        {
            return JsonSerializer.Serialize(value, _jsonSerializerOptions);
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

        private static string DescribeTriggerKind(string value)
        {
            return value switch
            {
                "user_message" => "ユーザー入力",
                "capability_result" => "能力の実行結果",
                "wake" => "API起床",
                "background_wake" => "定期起床",
                "" => string.Empty,
                _ => value,
            };
        }

        private static string DescribeResultKind(string value)
        {
            return value switch
            {
                "speech" => "発話",
                "noop" => "見送り",
                "capability_request" => "能力実行",
                "internal_failure" => "内部失敗",
                "pending_intent" => "保留",
                "" => string.Empty,
                _ => value,
            };
        }

        private static string DescribeStatus(string value)
        {
            return value switch
            {
                "succeeded" => "成功",
                "failed" => "失敗",
                "skipped" => "スキップ",
                "queued" => "待機中",
                "not_started" => "未開始",
                "not_requested" => "要求なし",
                "missing" => "不足",
                "adopted" => "採用",
                "rejected" => "不採用",
                "" => string.Empty,
                _ => value,
            };
        }

        private static string DescribeBool(string value)
        {
            return value switch
            {
                "true" => "あり",
                "false" => "なし",
                "" => string.Empty,
                _ => value,
            };
        }

        private sealed class CycleSummaryListItem
        {
            public CycleSummaryListItem(OtomeKairoCycleSummary summary)
            {
                CycleId = summary.CycleId;
                TriggerKind = DescribeTriggerKind(summary.TriggerKind);
                ResultKind = DescribeResultKind(summary.ResultKind);
                Failed = summary.Failed;
                StartedAtDisplay = TryFormatTimestamp(summary.StartedAt);
            }

            public string CycleId { get; }

            public string StartedAtDisplay { get; }

            public string TriggerKind { get; }

            public string ResultKind { get; }

            public bool Failed { get; }

            public string FailedDisplay => Failed ? "×" : string.Empty;

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
