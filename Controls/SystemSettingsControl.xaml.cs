using CocoroConsole.Communication;
using CocoroConsole.Models;
using CocoroConsole.Models.CocoroGhostApi;
using CocoroConsole.Services;
using CocoroConsole.Windows;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace CocoroConsole.Controls
{
    /// <summary>
    /// SystemSettingsControl.xaml の相互作用ロジック
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
        /// APIから取得したreminders
        /// </summary>
        private List<CocoroGhostReminder> _apiReminders = new();

        /// <summary>
        /// 表示用ID -> APIリマインダーの対応
        /// </summary>
        private readonly Dictionary<int, CocoroGhostReminder> _reminderIdMap = new();

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
                await LoadSettingsFromApiAsync(appSettings);

                // スクショ除外（ウィンドウタイトル正規表現 / ローカル設定）
                ExcludeWindowTitlePatternsTextBox.Text = string.Join(
                    Environment.NewLine,
                    appSettings.ScreenshotSettings.excludePatterns ?? new List<string>()
                );

                // マイク設定
                MicThresholdSlider.Value = appSettings.MicrophoneSettings.inputThreshold;

                // 話者識別設定
                var dbPath = System.IO.Path.Combine(appSettings.UserDataDirectory, "speaker_recognition.db");
                var speakerService = new SpeakerRecognitionService(dbPath, appSettings.MicrophoneSettings.speakerRecognitionThreshold);
                SpeakerManagementControl.Initialize(speakerService, appSettings.MicrophoneSettings.speakerRecognitionThreshold);

                // Bearer Token設定
                BearerTokenPasswordBox.Text = appSettings.CocoroGhostBearerToken ?? string.Empty;

                // リマインダーUI初期化（スペース区切り形式）
                ReminderDateTimeTextBox.Text = DateTime.Now.AddHours(1).ToString("yyyy-MM-dd HH:mm");

                // イベントハンドラーを設定
                SetupEventHandlers();

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

        public int GetDesktopWatchIntervalSeconds()
        {
            if (int.TryParse(DesktopWatchIntervalSecondsTextBox.Text, out var seconds) && seconds > 0)
            {
                return seconds;
            }

            return 300;
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
        /// APIから設定を読み込み（reminders_enabled / reminders / desktop_watch）
        /// </summary>
        private async Task LoadSettingsFromApiAsync(IAppSettings appSettings)
        {
            try
            {
                if (_apiClient != null)
                {
                    var settings = await _apiClient.GetSettingsAsync();
                    if (settings != null)
                    {
                        _apiReminders = settings.Reminders ?? new List<CocoroGhostReminder>();
                        EnableReminderCheckBox.IsChecked = settings.RemindersEnabled;

                        DesktopWatchEnabledCheckBox.IsChecked = settings.DesktopWatchEnabled;
                        DesktopWatchIntervalSecondsTextBox.Text = (settings.DesktopWatchIntervalSeconds > 0 ? settings.DesktopWatchIntervalSeconds : 300).ToString();

                        UpdateReminderListUI();
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"APIから設定の読み込みに失敗: {ex.Message}");
            }

            // APIが利用できない場合はローカル設定を使用
            EnableReminderCheckBox.IsChecked = false;
            DesktopWatchEnabledCheckBox.IsChecked = false;
            DesktopWatchIntervalSecondsTextBox.Text = "300";
            _apiReminders = new List<CocoroGhostReminder>();
            UpdateReminderListUI();
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
        /// 現在のリマインダー一覧（API形式）を取得
        /// </summary>
        public List<CocoroGhostReminder> GetReminders()
        {
            return _apiReminders.ToList();
        }

        private static bool TryParseScheduledAt(string scheduledAt, out DateTimeOffset dateTimeOffset)
        {
            return DateTimeOffset.TryParse(
                scheduledAt,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out dateTimeOffset
            );
        }

        private static string ToApiScheduledAtUtcString(DateTime localDateTime)
        {
            var local = DateTime.SpecifyKind(localDateTime, DateTimeKind.Local);
            var utc = local.ToUniversalTime();
            return utc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        }

        private void UpdateReminderListUI()
        {
            _reminderIdMap.Clear();

            var items = new List<Reminder>();
            var sorted = _apiReminders
                .Select(r =>
                {
                    if (TryParseScheduledAt(r.ScheduledAt, out var dto))
                    {
                        return (Reminder: r, SortKey: dto.UtcDateTime);
                    }
                    return (Reminder: r, SortKey: DateTime.MaxValue);
                })
                .OrderByDescending(x => x.SortKey)
                .Select(x => x.Reminder)
                .ToList();

            int id = 1;
            foreach (var reminder in sorted)
            {
                string displayTime = reminder.ScheduledAt;
                if (TryParseScheduledAt(reminder.ScheduledAt, out var dto))
                {
                    displayTime = dto.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                }

                _reminderIdMap[id] = reminder;
                items.Add(new Reminder
                {
                    Id = id,
                    RemindDatetime = displayTime,
                    Requirement = reminder.Content
                });
                id++;
            }

            Dispatcher.Invoke(() => { RemindersItemsControl.ItemsSource = items; });
        }

        /// <summary>
        /// リマインダー追加ボタンクリック
        /// </summary>
        private void AddReminderButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dateTimeText = ReminderDateTimeTextBox.Text.Trim();
                var messageText = ReminderMessageTextBox.Text.Trim();

                if (string.IsNullOrEmpty(dateTimeText))
                {
                    MessageBox.Show("予定日時を入力してください。", "入力エラー",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (string.IsNullOrEmpty(messageText))
                {
                    MessageBox.Show("メッセージを入力してください。", "入力エラー",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // スペース区切り形式の解析
                DateTime scheduledAt;
                if (!DateTime.TryParseExact(dateTimeText, new[] { "yyyy-MM-dd HH:mm", "yyyy-M-d H:mm", "yyyy-MM-dd HH:mm:ss", "yyyy-M-d H:mm:ss" },
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None,
                    out scheduledAt))
                {
                    MessageBox.Show("日時の形式が正しくありません。\nYYYY-MM-DD HH:MM の形式で入力してください。",
                        "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (scheduledAt <= DateTime.Now)
                {
                    MessageBox.Show("過去の時刻は設定できません。", "入力エラー",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                _apiReminders.Add(new CocoroGhostReminder
                {
                    ScheduledAt = ToApiScheduledAtUtcString(scheduledAt),
                    Content = messageText
                });

                UpdateReminderListUI();
                MarkSettingsChanged();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"リマインダー追加エラー: {ex.Message}", "エラー",
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
                if (sender is not Button button || button.Tag is not int reminderId) return;
                if (!_reminderIdMap.TryGetValue(reminderId, out var apiReminder)) return;

                _apiReminders.Remove(apiReminder);
                UpdateReminderListUI();
                MarkSettingsChanged();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"リマインダー削除エラー: {ex.Message}", "エラー",
                    MessageBoxButton.OK, MessageBoxImage.Error);
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
