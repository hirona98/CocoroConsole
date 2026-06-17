using CocoroConsole.Models.OtomeKairoApi;
using CocoroConsole.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace CocoroConsole.Windows
{
    /// <summary>
    /// OtomeKairo の自律実行 run を確認する読み取り専用ビューアー。
    /// </summary>
    public partial class AutonomousRunViewerWindow : Window
    {
        private readonly ObservableCollection<AutonomousRunListItem> _runItems = new ObservableCollection<AutonomousRunListItem>();
        private readonly DispatcherTimer _autoRefreshTimer;
        private OtomeKairoApiClient? _apiClient;
        private CancellationTokenSource? _loadCts;
        private bool _isLoading;

        public bool IsClosed { get; private set; }

        public AutonomousRunViewerWindow()
        {
            _autoRefreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3),
            };
            _autoRefreshTimer.Tick += AutoRefreshTimer_Tick;
            InitializeComponent();
            RunListDataGrid.ItemsSource = _runItems;
            Loaded += AutonomousRunViewerWindow_Loaded;
            Closed += AutonomousRunViewerWindow_Closed;
        }

        private async void AutonomousRunViewerWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadAutonomousRunsAsync(preserveSelection: false);
            if (!IsClosed && IsAutoRefreshEnabled())
            {
                _autoRefreshTimer.Start();
            }
        }

        private void AutonomousRunViewerWindow_Closed(object? sender, EventArgs e)
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
            await LoadAutonomousRunsAsync(preserveSelection: true, updateRefreshButtonState: true);
        }

        private async void AutoRefreshTimer_Tick(object? sender, EventArgs e)
        {
            await LoadAutonomousRunsAsync(preserveSelection: true, updateRefreshButtonState: false);
        }

        private void AutoRefreshCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (IsClosed)
            {
                return;
            }

            var isEnabled = sender is CheckBox checkBox
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

            if (StatusTextBlock != null)
            {
                UpdateStatus($"自律実行一覧を表示します。自動更新: {AutoRefreshStatusText()}");
            }
        }

        private void RunListDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading)
            {
                return;
            }

            UpdateSelectedRunDetails();
        }

        private async Task LoadAutonomousRunsAsync(bool preserveSelection, bool updateRefreshButtonState = true)
        {
            if (_isLoading)
            {
                return;
            }

            _isLoading = true;
            if (updateRefreshButtonState)
            {
                RefreshButton.IsEnabled = false;
            }

            try
            {
                var selectedRunId = preserveSelection && RunListDataGrid.SelectedItem is AutonomousRunListItem current
                    ? current.RunId
                    : null;
                var client = EnsureApiClient();
                if (client == null)
                {
                    ClearView("OtomeKairo の token または base URL が未設定です。");
                    return;
                }

                _loadCts?.Cancel();
                _loadCts?.Dispose();
                _loadCts = new CancellationTokenSource();

                if (_runItems.Count == 0)
                {
                    UpdateStatus("自律実行 run 一覧を読み込み中...");
                }

                var response = await client.GetAutonomousRunsAsync(_loadCts.Token).ConfigureAwait(true);
                var runs = response.AutonomousRuns ?? new List<OtomeKairoAutonomousRunSummary>();
                ApplyRuns(runs);
                UpdateSummary(response.GeneratedAt);
                UpdateStatus($"自律実行一覧を表示中: 件数={_runItems.Count} / 最終取得: {DisplayDateTime(response.GeneratedAt)} / 自動更新: {AutoRefreshStatusText()}");

                if (_runItems.Count == 0)
                {
                    ClearSelectedRun("自律実行 run はありません。");
                    return;
                }

                var nextSelection = ResolveSelection(selectedRunId);
                if (nextSelection != null && !ReferenceEquals(RunListDataGrid.SelectedItem, nextSelection))
                {
                    RunListDataGrid.SelectedItem = nextSelection;
                    RunListDataGrid.ScrollIntoView(nextSelection);
                }
                UpdateSelectedRunDetails();
            }
            catch (OperationCanceledException)
            {
                UpdateStatus("自律実行 run 一覧の読み込みをキャンセルしました。");
            }
            catch (Exception ex)
            {
                UpdateStatus($"自律実行 run 一覧の読み込みに失敗しました: {ex.Message}");
                ClearView($"自律実行 run 一覧の読み込みに失敗しました。\n{ex.Message}");
            }
            finally
            {
                _isLoading = false;
                if (updateRefreshButtonState)
                {
                    RefreshButton.IsEnabled = true;
                }
            }
        }

        private void ApplyRuns(IReadOnlyList<OtomeKairoAutonomousRunSummary> runs)
        {
            var nextRuns = runs
                .Where(run => !string.IsNullOrWhiteSpace(run.RunId))
                .ToList();
            var idsChanged = _runItems.Count != nextRuns.Count;
            if (!idsChanged)
            {
                for (var index = 0; index < nextRuns.Count; index++)
                {
                    if (!string.Equals(_runItems[index].RunId, nextRuns[index].RunId, StringComparison.Ordinal))
                    {
                        idsChanged = true;
                        break;
                    }
                }
            }

            if (idsChanged)
            {
                _runItems.Clear();
                foreach (var run in nextRuns)
                {
                    _runItems.Add(new AutonomousRunListItem(run));
                }
                return;
            }

            for (var index = 0; index < nextRuns.Count; index++)
            {
                _runItems[index].Update(nextRuns[index]);
            }
        }

        private AutonomousRunListItem? ResolveSelection(string? selectedRunId)
        {
            if (!string.IsNullOrWhiteSpace(selectedRunId))
            {
                var selected = _runItems.FirstOrDefault(item => string.Equals(item.RunId, selectedRunId, StringComparison.Ordinal));
                if (selected != null)
                {
                    return selected;
                }
            }

            if (RunListDataGrid.SelectedItem is AutonomousRunListItem current && _runItems.Contains(current))
            {
                return current;
            }

            return _runItems.FirstOrDefault();
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
            _runItems.Clear();
            UpdateSummary(null);
            ClearSelectedRun(message);
        }

        private void ClearSelectedRun(string message)
        {
            SetText(SelectedRunTitleTextBlock, "run 未選択");
            SetText(SelectedRunMetaTextBlock, string.Empty);
            SetText(SelectedRunDetailsTextBox, message);
        }

        private void UpdateSelectedRunDetails()
        {
            if (RunListDataGrid.SelectedItem is not AutonomousRunListItem selected)
            {
                ClearSelectedRun("run を選択してください。");
                return;
            }

            var run = selected.Run;
            SetText(SelectedRunTitleTextBlock, selected.ObjectiveDisplay);
            SetText(
                SelectedRunMetaTextBlock,
                $"状態: {DescribeStatus(run.Status)} / 起点: {DescribeOriginKind(run.OriginKind)} / 更新: {DisplayDateTime(run.UpdatedAt)}");
            SetText(SelectedRunDetailsTextBox, BuildRunDetails(run));
        }

        private void UpdateStatus(string message)
        {
            SetText(StatusTextBlock, message);
        }

        private bool IsAutoRefreshEnabled()
        {
            return AutoRefreshCheckBox.IsChecked == true;
        }

        private string AutoRefreshStatusText()
        {
            return IsAutoRefreshEnabled() ? "3秒" : "停止";
        }

        private void UpdateSummary(string? generatedAt)
        {
            var activeCount = CountByStatus("active");
            var waitingCount = CountByStatus("waiting_timer", "waiting_result");
            var pausedCount = CountByStatus("paused");
            var completedCount = CountByStatus("completed");
            var cancelledCount = CountByStatus("cancelled");
            var latestText = string.IsNullOrWhiteSpace(generatedAt)
                ? "未取得"
                : DisplayDateTime(generatedAt);
            SetText(
                SummaryTextBlock,
                $"件数: {_runItems.Count} / 進行中: {activeCount} / 待機中: {waitingCount} / 一時停止: {pausedCount} / 完了: {completedCount} / キャンセル: {cancelledCount} / 最終取得: {latestText}");
        }

        private int CountByStatus(params string[] statuses)
        {
            return _runItems.Count(item => statuses.Contains(item.StatusRaw, StringComparer.Ordinal));
        }

        private static string BuildRunDetails(OtomeKairoAutonomousRunSummary run)
        {
            var builder = new StringBuilder();
            builder.AppendLine($"目的: {DisplayValue(run.ObjectiveSummary)}");
            builder.AppendLine($"今の動き: {DisplayValue(run.CurrentStepSummary)}");
            builder.AppendLine($"これまでの経過: {DisplayValue(run.HistorySummary)}");
            builder.AppendLine();
            builder.AppendLine($"次の予定: {DisplayDateTime(run.NextRunAt)}");
            builder.AppendLine($"待機中の能力 request ID: {DisplayValue(run.WaitingRequestId)}");
            builder.AppendLine($"一時停止理由: {DescribePauseReason(run.PauseReason)}");
            builder.AppendLine();
            builder.AppendLine($"作成: {DisplayDateTime(run.CreatedAt)}");
            builder.AppendLine($"更新: {DisplayDateTime(run.UpdatedAt)}");
            builder.AppendLine($"完了: {DisplayDateTime(run.CompletedAt)}");
            builder.AppendLine();
            builder.AppendLine($"status: {DisplayValue(run.Status)}");
            builder.AppendLine($"origin_kind: {DisplayValue(run.OriginKind)}");
            return builder.ToString();
        }

        private static string DescribeStatus(string? status)
        {
            if (string.IsNullOrWhiteSpace(status))
            {
                return "（なし）";
            }

            return status.Trim() switch
            {
                "active" => "実行中",
                "waiting_timer" => "タイマー待機",
                "waiting_result" => "能力結果待ち",
                "paused" => "一時停止",
                "completed" => "完了",
                "cancelled" => "キャンセル",
                _ => status.Trim(),
            };
        }

        private static string DescribeOriginKind(string? originKind)
        {
            if (string.IsNullOrWhiteSpace(originKind))
            {
                return "（なし）";
            }

            return originKind.Trim() switch
            {
                "user_message" => "ユーザー依頼",
                "background_wake" => "定期起床",
                "wake" => "手動起床",
                "capability_result" => "能力結果",
                _ => originKind.Trim(),
            };
        }

        private static string DescribePauseReason(string? pauseReason)
        {
            if (string.IsNullOrWhiteSpace(pauseReason))
            {
                return "（なし）";
            }

            return pauseReason.Trim() switch
            {
                "paused_by_user_interaction" => "ユーザー対応中",
                "manual_pause" => "手動一時停止",
                _ => pauseReason.Trim(),
            };
        }

        private static string DisplayValue(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? "（なし）" : value.Trim();
        }

        private static string DisplayDateTime(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "（なし）";
            }

            if (DateTimeOffset.TryParse(value.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp))
            {
                return timestamp.ToLocalTime().ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture);
            }

            return value.Trim();
        }

        private static string DisplayWaitingRequest(string? waitingRequestId)
        {
            return string.IsNullOrWhiteSpace(waitingRequestId) ? "なし" : "結果待ち";
        }

        private static void SetText(TextBlock textBlock, string value)
        {
            if (!string.Equals(textBlock.Text, value, StringComparison.Ordinal))
            {
                textBlock.Text = value;
            }
        }

        private static void SetText(TextBox textBox, string value)
        {
            if (!string.Equals(textBox.Text, value, StringComparison.Ordinal))
            {
                textBox.Text = value;
            }
        }

        public sealed class AutonomousRunListItem : INotifyPropertyChanged
        {
            private OtomeKairoAutonomousRunSummary _run;

            public AutonomousRunListItem(OtomeKairoAutonomousRunSummary run)
            {
                _run = run;
            }

            public event PropertyChangedEventHandler? PropertyChanged;

            public OtomeKairoAutonomousRunSummary Run => _run;

            public string RunId => _run.RunId;

            public string StatusRaw => _run.Status;

            public string StatusDisplay => DescribeStatus(_run.Status);

            public string ObjectiveDisplay => DisplayValue(_run.ObjectiveSummary);

            public string CurrentStepDisplay => DisplayValue(_run.CurrentStepSummary);

            public string NextRunAtDisplay => DisplayDateTime(_run.NextRunAt);

            public string WaitingRequestIdDisplay => DisplayWaitingRequest(_run.WaitingRequestId);

            public string UpdatedAtDisplay => DisplayDateTime(_run.UpdatedAt);

            public string OriginKindDisplay => DescribeOriginKind(_run.OriginKind);

            public void Update(OtomeKairoAutonomousRunSummary run)
            {
                if (HasSameValues(_run, run))
                {
                    _run = run;
                    return;
                }

                _run = run;
                NotifyAllPropertiesChanged();
            }

            private static bool HasSameValues(OtomeKairoAutonomousRunSummary left, OtomeKairoAutonomousRunSummary right)
            {
                return string.Equals(left.RunId, right.RunId, StringComparison.Ordinal)
                    && string.Equals(left.MemorySetId, right.MemorySetId, StringComparison.Ordinal)
                    && string.Equals(left.Status, right.Status, StringComparison.Ordinal)
                    && string.Equals(left.ObjectiveSummary, right.ObjectiveSummary, StringComparison.Ordinal)
                    && string.Equals(left.OriginKind, right.OriginKind, StringComparison.Ordinal)
                    && string.Equals(left.CurrentStepSummary, right.CurrentStepSummary, StringComparison.Ordinal)
                    && string.Equals(left.HistorySummary, right.HistorySummary, StringComparison.Ordinal)
                    && string.Equals(left.NextRunAt, right.NextRunAt, StringComparison.Ordinal)
                    && string.Equals(left.WaitingRequestId, right.WaitingRequestId, StringComparison.Ordinal)
                    && string.Equals(left.PauseReason, right.PauseReason, StringComparison.Ordinal)
                    && string.Equals(left.CreatedAt, right.CreatedAt, StringComparison.Ordinal)
                    && string.Equals(left.UpdatedAt, right.UpdatedAt, StringComparison.Ordinal)
                    && string.Equals(left.CompletedAt, right.CompletedAt, StringComparison.Ordinal);
            }

            private void NotifyAllPropertiesChanged()
            {
                OnPropertyChanged(nameof(Run));
                OnPropertyChanged(nameof(RunId));
                OnPropertyChanged(nameof(StatusRaw));
                OnPropertyChanged(nameof(StatusDisplay));
                OnPropertyChanged(nameof(ObjectiveDisplay));
                OnPropertyChanged(nameof(CurrentStepDisplay));
                OnPropertyChanged(nameof(NextRunAtDisplay));
                OnPropertyChanged(nameof(WaitingRequestIdDisplay));
                OnPropertyChanged(nameof(UpdatedAtDisplay));
                OnPropertyChanged(nameof(OriginKindDisplay));
            }

            private void OnPropertyChanged(string propertyName)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }
}
