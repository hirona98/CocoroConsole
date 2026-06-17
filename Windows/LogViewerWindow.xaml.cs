using CocoroConsole.Communication;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Data;

namespace CocoroConsole.Windows
{
    /// <summary>
    /// ログビューアーウィンドウ
    /// </summary>
    public partial class LogViewerWindow : Window
    {
        private ObservableCollection<LogMessage> _allLogs = new ObservableCollection<LogMessage>();
        public ObservableCollection<ComponentFilterItem> ComponentFilters { get; } = new ObservableCollection<ComponentFilterItem>();
        private ICollectionView? _filteredLogs;
        private string _levelFilter = "";
        private const int MaxDisplayedLogs = 200;
        private readonly List<LogMessage> _pendingLogsWhileAutoScrollPaused = new List<LogMessage>();
        public bool IsClosed { get; private set; } = false;

        // スクロール位置保持用
        private ScrollViewer? _scrollViewer;

        public LogViewerWindow()
        {
            InitializeComponent();
            ComponentFilterItemsControl.ItemsSource = ComponentFilters;
            InitializeLogView();
            UpdateComponentFilterSummary();

            // 初期UI状態を設定
            Cursor = System.Windows.Input.Cursors.Arrow;
            LogDataGrid.IsEnabled = true;

            // DataGridがロードされた後にScrollViewerの参照を取得
            LogDataGrid.Loaded += (s, e) => InitializeScrollViewer();
        }

        /// <summary>
        /// ログビューの初期化
        /// </summary>
        private void InitializeLogView()
        {
            _filteredLogs = CollectionViewSource.GetDefaultView(_allLogs);
            _filteredLogs.Filter = LogFilter;

            LogDataGrid.ItemsSource = _filteredLogs;

            UpdateLogCount();
        }

        /// <summary>
        /// ScrollViewerの参照を初期化
        /// </summary>
        private void InitializeScrollViewer()
        {
            if (_scrollViewer == null)
            {
                _scrollViewer = GetScrollViewer(LogDataGrid);
            }
        }

        /// <summary>
        /// DataGridからScrollViewerを取得
        /// </summary>
        /// <param name="dataGrid">対象のDataGrid</param>
        /// <returns>ScrollViewer</returns>
        private ScrollViewer? GetScrollViewer(DataGrid dataGrid)
        {
            if (dataGrid == null) return null;

            // VisualTreeからScrollViewerを探す
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(dataGrid); i++)
            {
                var child = VisualTreeHelper.GetChild(dataGrid, i);
                var scrollViewer = FindScrollViewer(child);
                if (scrollViewer != null)
                {
                    return scrollViewer;
                }
            }
            return null;
        }

        /// <summary>
        /// VisualTree内でScrollViewerを再帰的に検索
        /// </summary>
        /// <param name="visual">検索対象</param>
        /// <returns>ScrollViewer</returns>
        private ScrollViewer? FindScrollViewer(DependencyObject visual)
        {
            if (visual is ScrollViewer scrollViewer)
            {
                return scrollViewer;
            }

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(visual); i++)
            {
                var child = VisualTreeHelper.GetChild(visual, i);
                var result = FindScrollViewer(child);
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }

        /// <summary>
        /// ログメッセージを追加（最大200件まで）
        /// </summary>
        /// <param name="logMessage">ログメッセージ</param>
        public void AddLogMessage(LogMessage logMessage)
        {
            AddLogMessages(new List<LogMessage> { logMessage });
        }

        /// <summary>
        /// ログメッセージをまとめて追加（最大200件まで）
        /// </summary>
        /// <param name="logMessages">ログメッセージのリスト</param>
        public void AddLogMessages(IReadOnlyList<LogMessage> logMessages)
        {
            if (logMessages == null || logMessages.Count == 0) return;

            Dispatcher.BeginInvoke(() =>
            {
                if (!IsAutoScrollEnabled())
                {
                    _pendingLogsWhileAutoScrollPaused.AddRange(logMessages);
                    while (_pendingLogsWhileAutoScrollPaused.Count > MaxDisplayedLogs)
                    {
                        _pendingLogsWhileAutoScrollPaused.RemoveAt(0);
                    }
                    return;
                }

                AppendLogMessagesToView(logMessages, scrollToLatest: true);
            });
        }

        private void AppendLogMessagesToView(IReadOnlyList<LogMessage> logMessages, bool scrollToLatest)
        {
            foreach (var logMessage in logMessages)
            {
                EnsureComponentFilter(logMessage.component);
                _allLogs.Add(logMessage);
            }

            while (_allLogs.Count > MaxDisplayedLogs)
            {
                _allLogs.RemoveAt(0);
            }

            UpdateLogCount();
            var latestLog = logMessages[logMessages.Count - 1];
            UpdateStatus($"最新ログ: {latestLog.timestamp:HH:mm:ss} [{latestLog.level}] {latestLog.component}");

            if (scrollToLatest && LogDataGrid.Items.Count > 0)
            {
                LogDataGrid.ScrollIntoView(LogDataGrid.Items[LogDataGrid.Items.Count - 1]);
            }
        }

        private void AutoScrollCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (!IsAutoScrollEnabled())
            {
                UpdateStatus("自動スクロール停止中");
                return;
            }

            if (_pendingLogsWhileAutoScrollPaused.Count == 0)
            {
                UpdateStatus("自動スクロール再開");
                return;
            }

            var pendingLogs = _pendingLogsWhileAutoScrollPaused.ToList();
            _pendingLogsWhileAutoScrollPaused.Clear();
            AppendLogMessagesToView(pendingLogs, scrollToLatest: true);
        }

        private bool IsAutoScrollEnabled()
        {
            return AutoScrollCheckBox.IsChecked == true;
        }

        /// <summary>
        /// 初期ログリストを一括で追加（UIスレッドで実行）
        /// </summary>
        /// <param name="logMessages">初期ログメッセージのリスト</param>
        public void LoadInitialLogs(List<LogMessage> logMessages)
        {
            // 既にUIスレッドで呼ばれることを前提とした処理
            _allLogs.Clear();
            _pendingLogsWhileAutoScrollPaused.Clear();
            ComponentFilters.Clear();

            foreach (var logMessage in logMessages)
            {
                EnsureComponentFilter(logMessage.component);
                _allLogs.Add(logMessage);
            }

            UpdateLogCount();

            // 最後のメッセージでステータス更新
            if (logMessages.Count > 0)
            {
                var lastMessage = logMessages.Last();
                UpdateStatus($"初期ログ読み込み完了: {logMessages.Count}件 - 最新: {lastMessage.timestamp:HH:mm:ss}");
            }
            else
            {
                UpdateStatus("ログファイルは空です");
            }

            // 自動スクロール（初期ロード時は常に最新にスクロール）
            if (AutoScrollCheckBox.IsChecked == true && LogDataGrid.Items.Count > 0)
            {
                LogDataGrid.ScrollIntoView(LogDataGrid.Items[LogDataGrid.Items.Count - 1]);
            }
        }

        /// <summary>
        /// ログフィルター
        /// </summary>
        /// <param name="item">フィルター対象のアイテム</param>
        /// <returns>表示するかどうか</returns>
        private bool LogFilter(object item)
        {
            if (item is not LogMessage log) return false;

            // レベルフィルター（指定レベル以上を表示）
            if (!string.IsNullOrEmpty(_levelFilter))
            {
                var logLevel = GetLogLevelPriority(log.level);
                var filterLevel = GetLogLevelPriority(_levelFilter);
                if (logLevel < filterLevel)
                    return false;
            }

            if (ComponentFilters.Count > 0)
            {
                var component = NormalizeComponent(log.component);
                if (!ComponentFilters.Any(filter => filter.Component == component && filter.IsSelected))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// ログレベルの優先度を取得（DEBUG=0, INFO=1, WARNING=2, ERROR=3）
        /// </summary>
        private static int GetLogLevelPriority(string level) => level switch
        {
            "DEBUG" => 0,
            "INFO" => 1,
            "WARNING" => 2,
            "ERROR" => 3,
            _ => -1
        };

        /// <summary>
        /// ログ件数を更新
        /// </summary>
        private void UpdateLogCount()
        {
            // UIが初期化されていない場合は何もしない
            if (LogCountTextBlock == null) return;

            var totalCount = _allLogs.Count;
            var filteredCount = _filteredLogs?.Cast<LogMessage>().Count() ?? 0;

            if (totalCount == filteredCount)
            {
                LogCountTextBlock.Text = $"総件数: {totalCount}";
            }
            else
            {
                LogCountTextBlock.Text = $"表示中: {filteredCount} / 総件数: {totalCount}";
            }
        }

        /// <summary>
        /// ステータスメッセージを更新
        /// </summary>
        /// <param name="message">ステータスメッセージ</param>
        private void UpdateStatus(string message)
        {
            // UIが初期化されていない場合は何もしない
            if (StatusTextBlock == null) return;

            StatusTextBlock.Text = message;
        }

        /// <summary>
        /// 外部からステータスラベルを更新する
        /// </summary>
        /// <param name="message">表示するメッセージ</param>
        public void UpdateStatusMessage(string message)
        {
            Dispatcher.BeginInvoke(() => UpdateStatus(message));
        }

        /// <summary>
        /// レベルフィルター変更イベント
        /// </summary>
        private void LevelFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // UIが完全に初期化されていない場合は何もしない
            if (LevelFilterComboBox?.SelectedItem is ComboBoxItem selectedItem)
            {
                // フィルター変更時のスクロール位置保持
                double savedOffset = 0;
                if (_scrollViewer != null && AutoScrollCheckBox.IsChecked != true)
                {
                    savedOffset = _scrollViewer.VerticalOffset;
                }

                _levelFilter = selectedItem.Tag?.ToString() ?? "";
                _filteredLogs?.Refresh();
                UpdateLogCount();

                // スクロール位置の復元
                if (_scrollViewer != null && AutoScrollCheckBox.IsChecked != true)
                {
                    Dispatcher.BeginInvoke(() =>
                    {
                        _scrollViewer.ScrollToVerticalOffset(savedOffset);
                    }, System.Windows.Threading.DispatcherPriority.Loaded);
                }
            }
        }

        /// <summary>
        /// コンポーネントフィルター変更イベント
        /// </summary>
        private void ComponentFilterCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            RefreshFiltersPreservingScroll();
            UpdateComponentFilterSummary();
        }

        /// <summary>
        /// スクロール位置を保持してフィルターを再適用する
        /// </summary>
        private void RefreshFiltersPreservingScroll()
        {
            double savedOffset = 0;
            if (_scrollViewer != null && AutoScrollCheckBox.IsChecked != true)
            {
                savedOffset = _scrollViewer.VerticalOffset;
            }

            _filteredLogs?.Refresh();
            UpdateLogCount();

            if (_scrollViewer != null && AutoScrollCheckBox.IsChecked != true)
            {
                Dispatcher.BeginInvoke(() =>
                {
                    _scrollViewer.ScrollToVerticalOffset(savedOffset);
                }, System.Windows.Threading.DispatcherPriority.Loaded);
            }
        }

        /// <summary>
        /// コンポーネントフィルター項目を追加する
        /// </summary>
        private void EnsureComponentFilter(string? component)
        {
            var normalizedComponent = NormalizeComponent(component);
            if (ComponentFilters.Any(filter => filter.Component == normalizedComponent)) return;

            ComponentFilters.Add(new ComponentFilterItem(normalizedComponent, isSelected: true));
            UpdateComponentFilterSummary();
        }

        /// <summary>
        /// コンポーネント名を表示用に正規化する
        /// </summary>
        private static string NormalizeComponent(string? component)
        {
            return string.IsNullOrWhiteSpace(component) ? "(未指定)" : component.Trim();
        }

        /// <summary>
        /// コンポーネントフィルターの選択状態を短く表示する
        /// </summary>
        private void UpdateComponentFilterSummary()
        {
            if (ComponentFilterButtonTextBlock == null) return;

            var totalCount = ComponentFilters.Count;
            var selectedCount = ComponentFilters.Count(filter => filter.IsSelected);
            var buttonText = $"{selectedCount} / {totalCount} 選択";

            ComponentFilterButtonTextBlock.Text = buttonText;
            ComponentFilterButtonTextBlock.ToolTip = buttonText;
            ComponentFilterToggleButton.ToolTip = buttonText;
        }

        /// <summary>
        /// クリアボタンクリックイベント
        /// </summary>
        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            _allLogs.Clear();
            ComponentFilters.Clear();
            UpdateComponentFilterSummary();
            UpdateLogCount();
            UpdateStatus("ログクリア");
        }

        /// <summary>
        /// ウィンドウが閉じられた時の処理
        /// </summary>
        /// <param name="e">イベント引数</param>
        protected override void OnClosed(EventArgs e)
        {
            IsClosed = true;
            base.OnClosed(e);
        }

    }

    public class ComponentFilterItem : INotifyPropertyChanged
    {
        private bool _isSelected = true;

        public ComponentFilterItem(string component, bool isSelected)
        {
            Component = component;
            _isSelected = isSelected;
        }

        public string Component { get; }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;

                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
