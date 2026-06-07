using CocoroConsole.Models.OtomeKairoApi;
using CocoroConsole.Services;
using System;
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
        private OtomeKairoApiClient? _apiClient;
        private CancellationTokenSource? _loadCts;
        private readonly DispatcherTimer _autoRefreshTimer;
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
            await LoadAutonomousRunsAsync(preserveSelection: true);
        }

        private async void AutoRefreshTimer_Tick(object? sender, EventArgs e)
        {
            await LoadAutonomousRunsAsync(preserveSelection: true);
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
                UpdateStatus($"自律実行 run 一覧を表示します。自動更新: {AutoRefreshStatusText()}");
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

        private async Task LoadAutonomousRunsAsync(bool preserveSelection)
        {
            if (_isLoading)
            {
                return;
            }

            _isLoading = true;
            RefreshButton.IsEnabled = false;

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

                UpdateStatus("自律実行 run 一覧を読み込み中...");
                var response = await client.GetAutonomousRunsAsync(_loadCts.Token).ConfigureAwait(true);
                var items = response.AutonomousRuns
                    .Select(run => new AutonomousRunListItem(run))
                    .ToList();

                RunListDataGrid.ItemsSource = items;
                UpdateStatus($"自律実行 run 一覧を表示中: 件数={items.Count} / 生成時刻={DisplayValue(response.GeneratedAt)} / 自動更新: {AutoRefreshStatusText()}");

                if (items.Count == 0)
                {
                    ClearSelectedRun("自律実行 run はありません。");
                    return;
                }

                var nextSelection = items.FirstOrDefault(item => item.RunId == selectedRunId) ?? items[0];
                RunListDataGrid.SelectedItem = nextSelection;
                RunListDataGrid.ScrollIntoView(nextSelection);
                UpdateSelectedRunDetails();
            }
            catch (OperationCanceledException)
            {
                UpdateStatus("自律実行 run 一覧の読み込みをキャンセルしました。");
            }
            catch (Exception ex)
            {
                UpdateStatus($"自律実行 run 一覧の読み込みに失敗しました: {ex.Message}");
                ClearView("自律実行 run 一覧の読み込みに失敗しました。");
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
            RunListDataGrid.ItemsSource = null;
            ClearSelectedRun(message);
        }

        private void ClearSelectedRun(string message)
        {
            SelectedRunTitleTextBlock.Text = "run 未選択";
            SelectedRunMetaTextBlock.Text = string.Empty;
            SelectedRunDetailsTextBox.Text = message;
        }

        private void UpdateSelectedRunDetails()
        {
            if (RunListDataGrid.SelectedItem is not AutonomousRunListItem selected)
            {
                ClearSelectedRun("run を選択してください。");
                return;
            }

            var run = selected.Run;
            SelectedRunTitleTextBlock.Text = DisplayValue(run.RunId);
            SelectedRunMetaTextBlock.Text = $"状態: {DescribeStatus(run.Status)} / 更新: {DisplayValue(run.UpdatedAt)}";
            SelectedRunDetailsTextBox.Text = BuildRunDetails(run);
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

        private static string BuildRunDetails(OtomeKairoAutonomousRunSummary run)
        {
            var builder = new StringBuilder();
            builder.AppendLine($"run_id: {DisplayValue(run.RunId)}");
            builder.AppendLine($"memory_set_id: {DisplayValue(run.MemorySetId)}");
            builder.AppendLine($"status: {DescribeStatus(run.Status)}");
            builder.AppendLine($"origin_kind: {DisplayValue(run.OriginKind)}");
            builder.AppendLine();
            builder.AppendLine($"objective_summary: {DisplayValue(run.ObjectiveSummary)}");
            builder.AppendLine($"current_step_summary: {DisplayValue(run.CurrentStepSummary)}");
            builder.AppendLine($"history_summary: {DisplayValue(run.HistorySummary)}");
            builder.AppendLine();
            builder.AppendLine($"next_run_at: {DisplayValue(run.NextRunAt)}");
            builder.AppendLine($"waiting_request_id: {DisplayValue(run.WaitingRequestId)}");
            builder.AppendLine($"pause_reason: {DisplayValue(run.PauseReason)}");
            builder.AppendLine();
            builder.AppendLine($"created_at: {DisplayValue(run.CreatedAt)}");
            builder.AppendLine($"updated_at: {DisplayValue(run.UpdatedAt)}");
            builder.AppendLine($"completed_at: {DisplayValue(run.CompletedAt)}");
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
                "active" => "実行中 (active)",
                "waiting_timer" => "タイマー待機 (waiting_timer)",
                "waiting_result" => "能力結果待ち (waiting_result)",
                "paused" => "一時停止 (paused)",
                "completed" => "完了 (completed)",
                "cancelled" => "キャンセル済み (cancelled)",
                _ => status.Trim(),
            };
        }

        private static string DisplayValue(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? "（なし）" : value.Trim();
        }

        private sealed class AutonomousRunListItem
        {
            public AutonomousRunListItem(OtomeKairoAutonomousRunSummary run)
            {
                Run = run;
            }

            public OtomeKairoAutonomousRunSummary Run { get; }

            public string RunId => Run.RunId;

            public string StatusDisplay => DescribeStatus(Run.Status);

            public string ObjectiveDisplay => DisplayValue(Run.ObjectiveSummary);

            public string CurrentStepDisplay => DisplayValue(Run.CurrentStepSummary);

            public string NextRunAtDisplay => DisplayValue(Run.NextRunAt);

            public string WaitingRequestIdDisplay => DisplayValue(Run.WaitingRequestId);

            public string UpdatedAtDisplay => DisplayValue(Run.UpdatedAt);

            public string OriginKindDisplay => DisplayValue(Run.OriginKind);
        }
    }
}
