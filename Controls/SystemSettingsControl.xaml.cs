using CocoroConsole.Communication;
using CocoroConsole.Models.CocoroGhostApi;
using CocoroConsole.Services;
using CocoroConsole.Windows;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace CocoroConsole.Controls
{
    /// <summary>
    /// SystemSettingsControl.xaml の相互作用ロジック。
    /// 
    /// 主に以下を扱う:
    /// - cocoro_ghost 側の設定読み込み/保存（desktop_watch 等）
    /// - リマインダーの一覧表示/編集（API同期あり）
    /// - ローカル設定（スクショ除外/マイク閾値/Bearer Token 等）の UI バインド
    /// </summary>
    public partial class SystemSettingsControl : UserControl
    {
        /// <summary>
        /// 設定が変更されたときに発生するイベント
        /// </summary>
        public event EventHandler? SettingsChanged;

        /// <summary>
        /// 読み込み完了フラグ
        /// </summary>
        private bool _isInitialized = false;

        /// <summary>
        /// cocoro_ghost API クライアント
        /// </summary>
        private CocoroGhostApiClient? _apiClient;

        /// <summary>
        /// cocoro_ghost のリマインダー API と同じ固定タイムゾーン（UI 表示・日時変換で使用）。
        /// </summary>
        private const string FixedTimeZone = "Asia/Tokyo";

        /// <summary>
        /// FixedTimeZone の UTC オフセット。
        /// （DateTimeOffset の相互変換に利用。DST を扱わない前提。）
        /// </summary>
        private static readonly TimeSpan FixedTimeZoneOffset = TimeSpan.FromHours(9);

        /// <summary>
        /// /api/reminders/settings から読み込んだ時点の RemindersEnabled。
        /// 変更検知・保存時の差分把握に使う。
        /// </summary>
        private bool _remindersEnabledBaseline = false;

        /// <summary>
        /// UI 上で編集されるリマインダー（サーバー未保存の新規も含む）。
        /// </summary>
        private readonly List<ReminderDraft> _reminderDrafts = new();

        /// <summary>
        /// API から読み込んだ初期状態のスナップショット（serverId→draft）。
        /// Patch が必要かどうかの差分判定に使う。
        /// </summary>
        private readonly Dictionary<string, ReminderDraft> _remindersBaselineById = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// サーバー上のリマインダーを削除対象としてマークした ID 一覧。
        /// </summary>
        private readonly HashSet<string> _deletedReminderIds = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 「追加」用のダイアログ（多重起動防止のため参照保持）。
        /// </summary>
        private ReminderEditDialog? _addReminderDialog;

        /// <summary>
        /// 「編集」ダイアログ（リマインダーごとに 1 つだけ表示するための管理）。
        /// </summary>
        private readonly Dictionary<string, ReminderEditDialog> _editReminderDialogs = new(StringComparer.OrdinalIgnoreCase);

        public SystemSettingsControl()
        {
            InitializeComponent();
        }

        /// <summary>
        /// cocoro_ghost APIクライアントを設定
        /// </summary>
        public void SetApiClient(CocoroGhostApiClient? apiClient)
        {
            _apiClient = apiClient;
        }

        /// <summary>
        /// 初期化処理
        /// </summary>
        public async Task InitializeAsync()
        {
            try
            {
                var appSettings = AppSettings.Instance;

                // /api/settings から設定を読み込み（API利用可能な場合）
                await LoadSystemSettingsFromApiAsync(appSettings);

                // /api/reminders からリマインダーを読み込み（API利用可能な場合）
                await LoadRemindersFromApiAsync();

                // スクショ除外（ウィンドウタイトル正規表現 / ローカル設定）
                ExcludeWindowTitlePatternsTextBox.Text = string.Join(
                    Environment.NewLine,
                    appSettings.ScreenshotSettings.excludePatterns ?? new List<string>()
                );

                // デスクトップウォッチ（アイドルタイムアウト / ローカル設定）
                var idleTimeoutMinutes = appSettings.ScreenshotSettings.idleTimeoutMinutes;
                if (idleTimeoutMinutes < 0)
                {
                    idleTimeoutMinutes = 10;
                }
                DesktopWatchIdleTimeoutMinutesTextBox.Text = idleTimeoutMinutes.ToString(CultureInfo.InvariantCulture);

                // マイク設定
                MicThresholdSlider.Value = appSettings.MicrophoneSettings.inputThreshold;

                // 話者識別設定
                var dbPath = System.IO.Path.Combine(appSettings.UserDataDirectory, "SpeakerRecognition.db");
                var speakerService = new SpeakerRecognitionService(dbPath, appSettings.MicrophoneSettings.speakerRecognitionThreshold);
                SpeakerManagementControl.Initialize(speakerService, appSettings.MicrophoneSettings.speakerRecognitionThreshold);

                // Bearer Token設定
                BearerTokenPasswordBox.Text = appSettings.CocoroGhostBearerToken ?? string.Empty;

                // イベントハンドラーを設定
                SetupEventHandlers();

                // 初期化完了後にのみ変更イベントを流す（InitializeAsync 中は OnSettingsChanged が発火しないようにする）
                _isInitialized = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"システム設定の初期化エラー: {ex.Message}", "エラー",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// イベントハンドラーを設定
        /// </summary>
        private void SetupEventHandlers()
        {
            // リマインダー有効/無効
            EnableReminderCheckBox.Checked += OnSettingsChanged;
            EnableReminderCheckBox.Unchecked += OnSettingsChanged;

            // スクショ除外（ウィンドウタイトル正規表現）
            ExcludeWindowTitlePatternsTextBox.TextChanged += OnSettingsChanged;

            // デスクトップウォッチ（cocoro_ghost側）
            DesktopWatchEnabledCheckBox.Checked += OnSettingsChanged;
            DesktopWatchEnabledCheckBox.Unchecked += OnSettingsChanged;
            DesktopWatchIntervalSecondsTextBox.TextChanged += OnSettingsChanged;

            // デスクトップウォッチ（アイドルタイムアウト / ローカル設定）
            DesktopWatchIdleTimeoutMinutesTextBox.TextChanged += OnSettingsChanged;

            // マイク設定
            MicThresholdSlider.ValueChanged += OnSettingsChanged;
        }

        /// <summary>
        /// 設定変更イベントハンドラー
        /// </summary>
        private void OnSettingsChanged(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized)
                return;

            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void MarkSettingsChanged()
        {
            if (!_isInitialized)
            {
                return;
            }

            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// リマインダーの有効状態を取得
        /// </summary>
        public bool GetIsEnableReminder()
        {
            return EnableReminderCheckBox.IsChecked ?? false;
        }

        /// <summary>
        /// リマインダーの有効状態を設定
        /// </summary>
        public void SetIsEnableReminder(bool enabled)
        {
            EnableReminderCheckBox.IsChecked = enabled;
        }

        public bool GetDesktopWatchEnabled()
        {
            return DesktopWatchEnabledCheckBox.IsChecked ?? false;
        }

        /// <summary>
        /// DesktopWatch の間隔（秒）を取得。
        /// 不正値の場合は既定値 300 秒を返す。
        /// </summary>
        public int GetDesktopWatchIntervalSeconds()
        {
            if (int.TryParse(DesktopWatchIntervalSecondsTextBox.Text, out var seconds) && seconds > 0)
            {
                return seconds;
            }

            return 300;
        }

        /// <summary>
        /// デスクトップウォッチのアイドルタイムアウト（分）を取得。
        /// 0 は無効。入力が不正な場合は既定値 10 分を返す。
        /// </summary>
        public int GetDesktopWatchIdleTimeoutMinutes()
        {
            if (!int.TryParse(DesktopWatchIdleTimeoutMinutesTextBox.Text, out var minutes))
            {
                return 10;
            }

            if (minutes < 0)
            {
                return 10;
            }

            return minutes;
        }

        /// <summary>
        /// マイク設定を取得
        /// </summary>
        public MicrophoneSettings GetMicrophoneSettings()
        {
            return new MicrophoneSettings
            {
                inputThreshold = (int)MicThresholdSlider.Value,
                speakerRecognitionThreshold = SpeakerManagementControl.GetCurrentThreshold()
            };
        }

        /// <summary>
        /// マイク設定を設定
        /// </summary>
        public void SetMicrophoneSettings(MicrophoneSettings settings)
        {
            MicThresholdSlider.Value = settings.inputThreshold;
            // speakerRecognitionThresholdはInitializeAsyncで設定済み
        }

        #region /api/settings API連携

        /// <summary>
        /// APIから設定を読み込み（desktop_watch）
        /// </summary>
        private async Task LoadSystemSettingsFromApiAsync(IAppSettings appSettings)
        {
            try
            {
                if (_apiClient != null)
                {
                    var settings = await _apiClient.GetSettingsAsync();
                    if (settings != null)
                    {
                        DesktopWatchEnabledCheckBox.IsChecked = settings.DesktopWatchEnabled;
                        DesktopWatchIntervalSecondsTextBox.Text = (settings.DesktopWatchIntervalSeconds > 0 ? settings.DesktopWatchIntervalSeconds : 300).ToString();
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"APIから設定の読み込みに失敗: {ex.Message}");
            }

            // APIが利用できない場合はローカル設定を使用
            DesktopWatchEnabledCheckBox.IsChecked = false;
            DesktopWatchIntervalSecondsTextBox.Text = "300";
        }

        /// <summary>
        /// スクショ除外（ウィンドウタイトル正規表現）のUI値を取得
        /// </summary>
        public List<string> GetWindowTitleExcludePatterns()
        {
            return ExcludeWindowTitlePatternsTextBox.Text
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .Where(p => !string.IsNullOrEmpty(p))
                .ToList();
        }

        /// <summary>
        /// APIクライアントが設定されているか
        /// </summary>
        public bool HasApiClient => _apiClient != null;

        #endregion

        #region リマインダー関連メソッド

        /// <summary>
        /// リマインダー設定/一覧をAPIから読み込み（失敗時は無効化）
        /// </summary>
        private async Task LoadRemindersFromApiAsync()
        {
            try
            {
                if (_apiClient == null)
                {
                    // API が使えない場合は UI を無効状態（読み込み失敗扱い）に寄せる
                    EnableReminderCheckBox.IsChecked = false;
                    _remindersEnabledBaseline = false;
                    _reminderDrafts.Clear();
                    _remindersBaselineById.Clear();
                    _deletedReminderIds.Clear();
                    UpdateReminderListUI();
                    return;
                }

                var settings = await _apiClient.GetRemindersSettingsAsync();
                EnableReminderCheckBox.IsChecked = settings?.RemindersEnabled ?? false;
                _remindersEnabledBaseline = EnableReminderCheckBox.IsChecked ?? false;

                var items = await _apiClient.GetRemindersAsync();
                _reminderDrafts.Clear();
                _remindersBaselineById.Clear();
                _deletedReminderIds.Clear();

                foreach (var item in items)
                {
                    if (string.IsNullOrWhiteSpace(item.Id))
                    {
                        continue;
                    }

                    var draft = ReminderDraft.FromApi(item);
                    _reminderDrafts.Add(draft);
                    _remindersBaselineById[item.Id] = draft.Clone();
                }

                UpdateReminderListUI();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"APIからリマインダーの読み込みに失敗: {ex.Message}");
                EnableReminderCheckBox.IsChecked = false;
                _remindersEnabledBaseline = false;
                _reminderDrafts.Clear();
                _remindersBaselineById.Clear();
                _deletedReminderIds.Clear();
                UpdateReminderListUI();
            }
        }

        /// <summary>
        /// 現在の UI 編集内容を /api/reminders に反映する。
        /// 
        /// 実行内容:
        /// - settings の更新（enabled）
        /// - 削除対象 ID の削除
        /// - 新規 draft の作成（serverId が無いもの）
        /// - baseline と差分がある既存 draft の Patch
        /// - 最後に API から再読み込みして baseline を更新
        /// </summary>
        public async Task SaveRemindersToApiAsync()
        {
            if (_apiClient == null)
            {
                return;
            }

            var enabled = EnableReminderCheckBox.IsChecked ?? false;
            await _apiClient.UpdateRemindersSettingsAsync(new CocoroGhostRemindersSettingsUpdateRequest
            {
                RemindersEnabled = enabled
            });

            foreach (var id in _deletedReminderIds.ToList())
            {
                await _apiClient.DeleteReminderAsync(id);
            }

            foreach (var draft in _reminderDrafts.Where(d => d.ServerId == null).ToList())
            {
                var created = await _apiClient.CreateReminderAsync(draft.ToCreateRequest());
                if (!string.IsNullOrWhiteSpace(created.Id))
                {
                    draft.ServerId = created.Id.Trim();
                    draft.LocalId = draft.ServerId;
                }
            }

            foreach (var draft in _reminderDrafts.Where(d => !string.IsNullOrWhiteSpace(d.ServerId)).ToList())
            {
                var id = draft.ServerId!;
                if (!_remindersBaselineById.TryGetValue(id, out var baseline))
                {
                    continue;
                }

                if (!baseline.IsSameAs(draft))
                {
                    await _apiClient.PatchReminderAsync(id, draft.ToPatchRequest());
                }
            }

            await LoadRemindersFromApiAsync();
        }

        /// <summary>
        /// _reminderDrafts を表示用の行（ReminderRow）に整形し、次回発火時刻でソートして ItemsSource を更新する。
        /// </summary>
        private void UpdateReminderListUI()
        {
            var rows = _reminderDrafts
                .Select(d =>
                {
                    var sortKey = d.NextFireAtUtc ?? long.MaxValue;

                    var row = new ReminderRow
                    {
                        LocalId = d.LocalId,
                        Enabled = d.Enabled,
                        NextFireAtLocal = FormatNextFireAtUtc(d.NextFireAtUtc),
                        RuleText = BuildRuleText(d),
                        Content = d.Content
                    };

                    return (Row: row, SortKey: sortKey);
                })
                .OrderBy(x => x.SortKey)
                .Select(x => x.Row)
                .ToList();

            Dispatcher.Invoke(() => { RemindersItemsControl.ItemsSource = rows; });
        }

        /// <summary>
        /// 次回発火 UTC 秒（Unix time）を「東京時刻」の表示文字列に変換する。
        /// 不正値/変換失敗時は空文字。
        /// </summary>
        private static string FormatNextFireAtUtc(long? nextFireAtUtc)
        {
            if (nextFireAtUtc == null || nextFireAtUtc <= 0)
            {
                return string.Empty;
            }

            try
            {
                var dto = DateTimeOffset.FromUnixTimeSeconds(nextFireAtUtc.Value).ToOffset(FixedTimeZoneOffset);
                return dto.ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture);
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string BuildRuleText(ReminderDraft draft)
        {
            // repeat_kind の値に応じて UI 表示（単発/毎日/毎週）を組み立てる
            var kind = (draft.RepeatKind ?? string.Empty).Trim().ToLowerInvariant();
            if (string.Equals(kind, "once", StringComparison.OrdinalIgnoreCase))
            {
                return "単発";
            }

            if (string.Equals(kind, "daily", StringComparison.OrdinalIgnoreCase))
            {
                return $"毎日 {draft.TimeOfDay}";
            }

            if (string.Equals(kind, "weekly", StringComparison.OrdinalIgnoreCase))
            {
                var list = (draft.Weekdays ?? new List<string>()).ToList();
                var jp = list
                    .Select(ToJapaneseWeekday)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .ToList();
                var suffix = jp.Count > 0 ? string.Join("・", jp) : "（未設定）";
                return $"毎週({suffix}) {draft.TimeOfDay}";
            }

            return draft.RepeatKind ?? string.Empty;
        }

        private static string ToJapaneseWeekday(string weekday)
        {
            var w = (weekday ?? string.Empty).Trim().ToLowerInvariant();
            return w switch
            {
                "sun" => "日",
                "mon" => "月",
                "tue" => "火",
                "wed" => "水",
                "thu" => "木",
                "fri" => "金",
                "sat" => "土",
                _ => string.Empty
            };
        }

        /// <summary>
        /// リマインダー追加ボタンクリック
        /// </summary>
        private void AddReminderButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_addReminderDialog != null && _addReminderDialog.IsVisible)
                {
                    _addReminderDialog.Activate();
                    return;
                }

                var owner = Window.GetWindow(this);
                _addReminderDialog = new ReminderEditDialog(
                    new ReminderEditResult
                    {
                        Enabled = true,
                        Content = string.Empty,
                        RepeatKind = "daily",
                        Hour = 9,
                        Minute = 0,
                        OnceDate = DateTime.Today,
                        Weekdays = null
                    },
                    ReminderDialogMode.Add,
                    onAdd: result =>
                    {
                        var draft = ReminderDraft.FromEditResult(result);
                        _reminderDrafts.Add(draft);
                        UpdateReminderListUI();
                        MarkSettingsChanged();
                    },
                    onOk: null
                );

                if (owner != null)
                {
                    _addReminderDialog.Owner = owner;
                }

                _addReminderDialog.Closed += (_, __) => { _addReminderDialog = null; };
                _addReminderDialog.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"リマインダー追加エラー: {ex.Message}", "エラー",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// リマインダー編集ボタンクリック
        /// </summary>
        private void EditReminderButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is not Button button || button.Tag is not string localId) return;
                var draft = _reminderDrafts.FirstOrDefault(d => string.Equals(d.LocalId, localId, StringComparison.OrdinalIgnoreCase));
                if (draft == null) return;

                if (_editReminderDialogs.TryGetValue(draft.LocalId, out var opened) && opened.IsVisible)
                {
                    opened.Activate();
                    return;
                }

                var dialog = new ReminderEditDialog(
                    draft.ToEditResult(),
                    ReminderDialogMode.Edit,
                    onAdd: null,
                    onOk: result =>
                    {
                        draft.ApplyEditResult(result);
                        UpdateReminderListUI();
                        MarkSettingsChanged();
                        _editReminderDialogs.Remove(draft.LocalId);
                    }
                );
                dialog.Owner = Window.GetWindow(this);
                dialog.Closed += (_, __) => { _editReminderDialogs.Remove(draft.LocalId); };
                _editReminderDialogs[draft.LocalId] = dialog;
                dialog.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"リマインダー編集エラー: {ex.Message}", "エラー",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// リマインダー削除ボタンクリック
        /// </summary>
        private void DeleteReminderButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is not Button button || button.Tag is not string localId) return;
                var draft = _reminderDrafts.FirstOrDefault(d => string.Equals(d.LocalId, localId, StringComparison.OrdinalIgnoreCase));
                if (draft == null) return;

                if (!string.IsNullOrWhiteSpace(draft.ServerId))
                {
                    // サーバーに存在するものは「削除対象」として記録し、保存時に Delete API を呼ぶ
                    _deletedReminderIds.Add(draft.ServerId!);
                }

                _reminderDrafts.Remove(draft);
                UpdateReminderListUI();
                MarkSettingsChanged();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"リマインダー削除エラー: {ex.Message}", "エラー",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ReminderEnabledCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (sender is not CheckBox checkBox || checkBox.Tag is not string localId) return;
            var draft = _reminderDrafts.FirstOrDefault(d => string.Equals(d.LocalId, localId, StringComparison.OrdinalIgnoreCase));
            if (draft == null) return;

            draft.Enabled = checkBox.IsChecked ?? false;
            MarkSettingsChanged();
        }

        /// <summary>
        /// ItemsControl に表示するための 1 行分 DTO。
        /// </summary>
        private sealed class ReminderRow
        {
            public string LocalId { get; set; } = string.Empty;
            public bool Enabled { get; set; }
            public string NextFireAtLocal { get; set; } = string.Empty;
            public string RuleText { get; set; } = string.Empty;
            public string Content { get; set; } = string.Empty;
        }

        /// <summary>
        /// UI 編集用のリマインダーモデル。
        /// 
        /// - ServerId: サーバー上の ID（null の場合は未保存新規）
        /// - LocalId: UI 上の識別子（新規作成時も一意になるよう付与）
        /// 
        /// cocoro_ghost の API モデルと、編集ダイアログ用モデルの橋渡しを担う。
        /// </summary>
        private sealed class ReminderDraft
        {
            public string LocalId { get; set; } = string.Empty;
            public string? ServerId { get; set; }
            public bool Enabled { get; set; }
            public string Content { get; set; } = string.Empty;
            public string RepeatKind { get; set; } = "daily"; // once|daily|weekly
            public string? ScheduledAt { get; set; } // repeat_kind=once
            public string? TimeOfDay { get; set; } = "09:00"; // daily/weekly
            public List<string>? Weekdays { get; set; }
            public long? NextFireAtUtc { get; set; }

            public static ReminderDraft FromApi(CocoroGhostReminderItem item)
            {
                // API から取得した値は repeat_kind を正規化して内部表現に寄せる
                var kind = (item.RepeatKind ?? "daily").Trim().ToLowerInvariant();
                return new ReminderDraft
                {
                    LocalId = item.Id,
                    ServerId = item.Id,
                    Enabled = item.Enabled,
                    Content = item.Content ?? string.Empty,
                    RepeatKind = kind,
                    ScheduledAt = item.ScheduledAt,
                    TimeOfDay = item.TimeOfDay ?? "09:00",
                    Weekdays = item.Weekdays?.ToList(),
                    NextFireAtUtc = item.NextFireAtUtc
                };
            }

            public static ReminderDraft FromEditResult(ReminderEditResult edit)
            {
                var draft = new ReminderDraft
                {
                    LocalId = $"new-{Guid.NewGuid()}",
                    ServerId = null,
                    Enabled = edit.Enabled,
                    Content = edit.Content ?? string.Empty,
                    RepeatKind = (edit.RepeatKind ?? "daily").Trim().ToLowerInvariant(),
                    Weekdays = edit.Weekdays?.ToList(),
                    NextFireAtUtc = null
                };

                // 入力値を API 互換の ScheduledAt / TimeOfDay に反映
                draft.ApplyEditResult(edit);
                return draft;
            }

            private static string ToTimeOfDayString(int hour, int minute)
            {
                hour = Math.Clamp(hour, 0, 23);
                minute = Math.Clamp(minute, 0, 59);
                return $"{hour:00}:{minute:00}";
            }

            private static string ToTokyoScheduledAtString(DateTime date, int hour, int minute)
            {
                // DateTimeKind.Unspecified で作り、Tokyo(+09:00) として DateTimeOffset 化する
                var dt = new DateTime(date.Year, date.Month, date.Day, Math.Clamp(hour, 0, 23), Math.Clamp(minute, 0, 59), 0, DateTimeKind.Unspecified);
                var dto = new DateTimeOffset(dt, FixedTimeZoneOffset);
                return dto.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);
            }

            public ReminderEditResult ToEditResult()
            {
                var kind = (RepeatKind ?? string.Empty).Trim().ToLowerInvariant();
                int hour = 9;
                int minute = 0;
                DateTime? onceDate = null;

                if (string.Equals(kind, "once", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrWhiteSpace(ScheduledAt) &&
                        DateTimeOffset.TryParse(ScheduledAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto))
                    {
                        var tokyo = dto.ToOffset(FixedTimeZoneOffset);
                        onceDate = tokyo.Date;
                        hour = tokyo.Hour;
                        minute = tokyo.Minute;
                    }
                    else if (NextFireAtUtc != null && NextFireAtUtc > 0)
                    {
                        var tokyo = DateTimeOffset.FromUnixTimeSeconds(NextFireAtUtc.Value).ToOffset(FixedTimeZoneOffset);
                        onceDate = tokyo.Date;
                        hour = tokyo.Hour;
                        minute = tokyo.Minute;
                    }
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(TimeOfDay))
                    {
                        var parts = TimeOfDay.Split(':');
                        if (parts.Length >= 2 &&
                            int.TryParse(parts[0], out var h) &&
                            int.TryParse(parts[1], out var m))
                        {
                            hour = Math.Clamp(h, 0, 23);
                            minute = Math.Clamp(m, 0, 59);
                        }
                    }
                }

                return new ReminderEditResult
                {
                    Enabled = Enabled,
                    Content = Content,
                    RepeatKind = string.IsNullOrWhiteSpace(kind) ? "daily" : kind,
                    Hour = hour,
                    Minute = minute,
                    OnceDate = onceDate,
                    Weekdays = Weekdays?.ToList()
                };
            }

            public void ApplyEditResult(ReminderEditResult edit)
            {
                Enabled = edit.Enabled;
                Content = edit.Content ?? string.Empty;
                RepeatKind = (edit.RepeatKind ?? "daily").Trim().ToLowerInvariant();
                Weekdays = edit.Weekdays?.ToList();

                if (string.Equals(RepeatKind, "once", StringComparison.OrdinalIgnoreCase))
                {
                    // 単発: ScheduledAt（日時）を持つ。TimeOfDay / Weekdays は使わない。
                    var date = edit.OnceDate ?? DateTime.Today;
                    ScheduledAt = ToTokyoScheduledAtString(date, edit.Hour, edit.Minute);
                    TimeOfDay = null;
                    Weekdays = null;
                }
                else
                {
                    // 繰り返し: TimeOfDay を持つ。weekly 以外は Weekdays を持たない。
                    ScheduledAt = null;
                    TimeOfDay = ToTimeOfDayString(edit.Hour, edit.Minute);
                    if (!string.Equals(RepeatKind, "weekly", StringComparison.OrdinalIgnoreCase))
                    {
                        Weekdays = null;
                    }
                }

                NextFireAtUtc = null;
            }

            public CocoroGhostReminderCreateRequest ToCreateRequest()
            {
                var kind = (RepeatKind ?? string.Empty).Trim().ToLowerInvariant();
                return new CocoroGhostReminderCreateRequest
                {
                    Enabled = Enabled,
                    RepeatKind = kind,
                    Content = Content,
                    ScheduledAt = string.Equals(kind, "once", StringComparison.OrdinalIgnoreCase) ? ScheduledAt : null,
                    TimeOfDay = (string.Equals(kind, "daily", StringComparison.OrdinalIgnoreCase) || string.Equals(kind, "weekly", StringComparison.OrdinalIgnoreCase)) ? TimeOfDay : null,
                    Weekdays = string.Equals(kind, "weekly", StringComparison.OrdinalIgnoreCase) ? Weekdays?.ToList() : null
                };
            }

            public CocoroGhostReminderPatchRequest ToPatchRequest()
            {
                var kind = (RepeatKind ?? string.Empty).Trim().ToLowerInvariant();
                return new CocoroGhostReminderPatchRequest
                {
                    Enabled = Enabled,
                    Content = Content,
                    RepeatKind = kind,
                    ScheduledAt = string.Equals(kind, "once", StringComparison.OrdinalIgnoreCase) ? ScheduledAt : null,
                    TimeOfDay = (string.Equals(kind, "daily", StringComparison.OrdinalIgnoreCase) || string.Equals(kind, "weekly", StringComparison.OrdinalIgnoreCase)) ? TimeOfDay : null,
                    Weekdays = string.Equals(kind, "weekly", StringComparison.OrdinalIgnoreCase) ? Weekdays?.ToList() : null
                };
            }

            public ReminderDraft Clone()
            {
                return new ReminderDraft
                {
                    LocalId = LocalId,
                    ServerId = ServerId,
                    Enabled = Enabled,
                    Content = Content,
                    RepeatKind = RepeatKind,
                    ScheduledAt = ScheduledAt,
                    TimeOfDay = TimeOfDay,
                    Weekdays = Weekdays?.ToList(),
                    NextFireAtUtc = NextFireAtUtc
                };
            }

            private static string NormalizeScheduledAt(string? scheduledAt)
            {
                if (string.IsNullOrWhiteSpace(scheduledAt))
                {
                    return string.Empty;
                }

                if (DateTimeOffset.TryParse(scheduledAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto))
                {
                    return dto.ToOffset(FixedTimeZoneOffset).ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);
                }

                return scheduledAt.Trim();
            }

            public bool IsSameAs(ReminderDraft other)
            {
                if (other == null) return false;
                if (Enabled != other.Enabled) return false;
                if (!string.Equals(Content ?? string.Empty, other.Content ?? string.Empty, StringComparison.Ordinal)) return false;

                var kindA = (RepeatKind ?? string.Empty).Trim().ToLowerInvariant();
                var kindB = (other.RepeatKind ?? string.Empty).Trim().ToLowerInvariant();
                if (!string.Equals(kindA, kindB, StringComparison.OrdinalIgnoreCase)) return false;

                if (string.Equals(kindA, "once", StringComparison.OrdinalIgnoreCase))
                {
                    // ScheduledAt はフォーマット差（UTC/ローカル/表記ゆれ）を吸収して比較する
                    return string.Equals(NormalizeScheduledAt(ScheduledAt), NormalizeScheduledAt(other.ScheduledAt), StringComparison.Ordinal);
                }

                if (!string.Equals((TimeOfDay ?? string.Empty).Trim(), (other.TimeOfDay ?? string.Empty).Trim(), StringComparison.Ordinal)) return false;

                if (!string.Equals(kindA, "weekly", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                var a = (Weekdays ?? new List<string>()).Select(x => x.Trim().ToLowerInvariant()).OrderBy(x => x).ToList();
                var b = (other.Weekdays ?? new List<string>()).Select(x => x.Trim().ToLowerInvariant()).OrderBy(x => x).ToList();
                return a.SequenceEqual(b);
            }
        }

        #endregion

        #region Bearer Token関連

        /// <summary>
        /// Bearer Token変更イベントハンドラー
        /// </summary>
        private void BearerTokenPasswordBox_PasswordChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isInitialized)
                return;

            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Bearer Tokenを取得
        /// </summary>
        public string GetBearerToken()
        {
            return BearerTokenPasswordBox.Text;
        }

        /// <summary>
        /// Bearer Tokenを設定
        /// </summary>
        public void SetBearerToken(string token)
        {
            BearerTokenPasswordBox.Text = token ?? string.Empty;
        }

        #endregion
    }
}
