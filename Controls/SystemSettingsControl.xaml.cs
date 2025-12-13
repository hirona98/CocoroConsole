using CocoroConsole.Communication;
using CocoroConsole.Models;
using CocoroConsole.Models.CocoroGhostApi;
using CocoroConsole.Services;
using CocoroConsole.Utilities;
using CocoroConsole.Windows;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
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
        /// リマインダーサービス
        /// </summary>
        private IReminderService _reminderService;

        /// <summary>
        /// cocoro_ghost API クライアント
        /// </summary>
        private CocoroGhostApiClient? _apiClient;

        /// <summary>
        /// APIから取得したexclude_keywords
        /// </summary>
        private List<string> _apiExcludeKeywords = new();

        /// <summary>
        /// APIから取得したLLMプリセット
        /// </summary>
        private List<LlmPreset> _apiLlmPresets = new();

        /// <summary>
        /// APIから取得したEmbeddingプリセット
        /// </summary>
        private List<EmbeddingPreset> _apiEmbeddingPresets = new();

        public SystemSettingsControl()
        {
            InitializeComponent();
            _reminderService = new ReminderService(AppSettings.Instance);
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

                // リマインダー有効状態を設定
                EnableReminderCheckBox.IsChecked = appSettings.IsEnableReminder;

                // デスクトップウォッチ設定
                ScreenshotEnabledCheckBox.IsChecked = appSettings.ScreenshotSettings.enabled;
                CaptureActiveWindowOnlyCheckBox.IsChecked = appSettings.ScreenshotSettings.captureActiveWindowOnly;
                ScreenshotIntervalTextBox.Text = appSettings.ScreenshotSettings.intervalMinutes.ToString();
                IdleTimeoutTextBox.Text = appSettings.ScreenshotSettings.idleTimeoutMinutes.ToString();

                // exclude_keywordsをAPIから読み込み（API利用可能な場合）
                await LoadExcludeKeywordsFromApiAsync(appSettings);

                // マイク設定
                MicThresholdSlider.Value = appSettings.MicrophoneSettings.inputThreshold;

                // 話者識別設定
                var dbPath = System.IO.Path.Combine(appSettings.UserDataDirectory, "speaker_recognition.db");
                var speakerService = new SpeakerRecognitionService(dbPath, appSettings.MicrophoneSettings.speakerRecognitionThreshold);
                SpeakerManagementControl.Initialize(speakerService, appSettings.MicrophoneSettings.speakerRecognitionThreshold);

                // Bearer Token設定
                BearerTokenPasswordBox.Password = appSettings.CocoroGhostBearerToken ?? string.Empty;

                // リマインダーUI初期化（スペース区切り形式）
                ReminderDateTimeTextBox.Text = DateTime.Now.AddHours(1).ToString("yyyy-MM-dd HH:mm");

                // リマインダーを読み込み
                await LoadRemindersAsync();

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

            // デスクトップウォッチ設定
            ScreenshotEnabledCheckBox.Checked += OnSettingsChanged;
            ScreenshotEnabledCheckBox.Unchecked += OnSettingsChanged;
            CaptureActiveWindowOnlyCheckBox.Checked += OnSettingsChanged;
            CaptureActiveWindowOnlyCheckBox.Unchecked += OnSettingsChanged;
            ScreenshotIntervalTextBox.TextChanged += OnSettingsChanged;
            IdleTimeoutTextBox.TextChanged += OnSettingsChanged;
            ExcludePatternsTextBox.TextChanged += OnSettingsChanged;

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

        /// <summary>
        /// スクリーンショット設定を取得
        /// </summary>
        public ScreenshotSettings GetScreenshotSettings()
        {
            var settings = new ScreenshotSettings
            {
                enabled = ScreenshotEnabledCheckBox.IsChecked ?? false,
                captureActiveWindowOnly = CaptureActiveWindowOnlyCheckBox.IsChecked ?? false
            };

            if (int.TryParse(ScreenshotIntervalTextBox.Text, out int interval))
            {
                settings.intervalMinutes = interval;
            }

            if (int.TryParse(IdleTimeoutTextBox.Text, out int timeout))
            {
                settings.idleTimeoutMinutes = timeout;
            }

            // 除外パターンを取得（空行を除外）
            var patterns = ExcludePatternsTextBox.Text
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .Where(p => !string.IsNullOrEmpty(p))
                .ToList();
            settings.excludePatterns = patterns;

            return settings;
        }

        /// <summary>
        /// スクリーンショット設定を設定
        /// </summary>
        public void SetScreenshotSettings(ScreenshotSettings settings)
        {
            ScreenshotEnabledCheckBox.IsChecked = settings.enabled;
            CaptureActiveWindowOnlyCheckBox.IsChecked = settings.captureActiveWindowOnly;
            ScreenshotIntervalTextBox.Text = settings.intervalMinutes.ToString();
            IdleTimeoutTextBox.Text = settings.idleTimeoutMinutes.ToString();
            ExcludePatternsTextBox.Text = string.Join(Environment.NewLine, settings.excludePatterns);
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

        #region exclude_keywords API連携

        /// <summary>
        /// APIからexclude_keywordsを読み込み
        /// </summary>
        private async Task LoadExcludeKeywordsFromApiAsync(IAppSettings appSettings)
        {
            try
            {
                if (_apiClient != null)
                {
                    var settings = await _apiClient.GetSettingsAsync();
                    if (settings != null)
                    {
                        _apiExcludeKeywords = settings.ExcludeKeywords ?? new List<string>();
                        _apiLlmPresets = settings.LlmPreset ?? new List<LlmPreset>();
                        _apiEmbeddingPresets = settings.EmbeddingPreset ?? new List<EmbeddingPreset>();
                        ExcludePatternsTextBox.Text = string.Join(Environment.NewLine, _apiExcludeKeywords);
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"APIからexclude_keywordsの読み込みに失敗: {ex.Message}");
            }

            // APIが利用できない場合はローカル設定を使用
            ExcludePatternsTextBox.Text = string.Join(Environment.NewLine, appSettings.ScreenshotSettings.excludePatterns);
        }

        /// <summary>
        /// exclude_keywordsをAPIに保存
        /// </summary>
        public async Task<bool> SaveExcludeKeywordsToApiAsync()
        {
            if (_apiClient == null) return false;

            try
            {
                var patterns = ExcludePatternsTextBox.Text
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(p => p.Trim())
                    .Where(p => !string.IsNullOrEmpty(p))
                    .ToList();

                // 最新設定を取得し、プリセットを保全したまま更新する
                var latestSettings = await _apiClient.GetSettingsAsync();
                if (latestSettings != null)
                {
                    _apiLlmPresets = latestSettings.LlmPreset ?? new List<LlmPreset>();
                    _apiEmbeddingPresets = latestSettings.EmbeddingPreset ?? new List<EmbeddingPreset>();
                }

                var request = new CocoroGhostSettingsUpdateRequest
                {
                    ExcludeKeywords = patterns,
                    LlmPreset = _apiLlmPresets,
                    EmbeddingPreset = _apiEmbeddingPresets
                };

                await _apiClient.UpdateSettingsAsync(request);
                _apiExcludeKeywords = patterns;
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"APIへのexclude_keywords保存に失敗: {ex.Message}");
                MessageBox.Show($"除外パターンのAPI保存に失敗しました: {ex.Message}", "警告",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
        }

        /// <summary>
        /// exclude_keywordsのUI値を取得
        /// </summary>
        public List<string> GetExcludeKeywords()
        {
            return ExcludePatternsTextBox.Text
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
        /// リマインダーリストを読み込み
        /// </summary>
        private async Task LoadRemindersAsync()
        {
            try
            {
                var reminders = await _reminderService.GetAllRemindersAsync();

                // UIスレッドで実行
                Dispatcher.Invoke(() =>
                {
                    RemindersItemsControl.ItemsSource = reminders;
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"リマインダー読み込みエラー: {ex.Message}");
            }
        }

        /// <summary>
        /// 現在時刻設定ボタンクリック
        /// </summary>
        private void SetCurrentTimeButton_Click(object sender, RoutedEventArgs e)
        {
            ReminderDateTimeTextBox.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        }

        /// <summary>
        /// リマインダー追加ボタンクリック
        /// </summary>
        private async void AddReminderButton_Click(object sender, RoutedEventArgs e)
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

                var reminder = new Reminder
                {
                    RemindDatetime = scheduledAt.ToString("yyyy-MM-dd HH:mm:ss"),
                    Requirement = messageText
                };

                await _reminderService.CreateReminderAsync(reminder);
                await LoadRemindersAsync();
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
        private async void DeleteReminderButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Button button && button.Tag is int reminderId)
                {
                    await _reminderService.DeleteReminderAsync(reminderId);
                    await LoadRemindersAsync();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"リマインダー削除エラー: {ex.Message}", "エラー",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// リマインダーリスト更新ボタンクリック
        /// </summary>
        private async void RefreshRemindersButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadRemindersAsync();
        }

        /// <summary>
        /// リマインダー入力テキストをパース
        /// </summary>
        /// <param name="input">入力文字列</param>
        /// <returns>予定時刻とメッセージのタプル</returns>
        private (DateTime?, string) ParseReminderInput(string input)
        {
            try
            {
                // 「○時間後に〜」「○分後に〜」形式
                var relativeMatch = Regex.Match(input, @"^(\d+)(時間|分)後に(.+)$");
                if (relativeMatch.Success)
                {
                    var amount = int.Parse(relativeMatch.Groups[1].Value);
                    var unit = relativeMatch.Groups[2].Value;
                    var message = relativeMatch.Groups[3].Value;

                    var scheduledAt = unit == "時間"
                        ? DateTime.Now.AddHours(amount)
                        : DateTime.Now.AddMinutes(amount);

                    return (scheduledAt, message);
                }

                // 「HH:mmに〜」形式
                var timeMatch = Regex.Match(input, @"^(\d{1,2}):(\d{2})に(.+)$");
                if (timeMatch.Success)
                {
                    var hour = int.Parse(timeMatch.Groups[1].Value);
                    var minute = int.Parse(timeMatch.Groups[2].Value);
                    var message = timeMatch.Groups[3].Value;

                    if (hour >= 0 && hour <= 23 && minute >= 0 && minute <= 59)
                    {
                        var now = DateTime.Now;
                        var scheduledAt = new DateTime(now.Year, now.Month, now.Day, hour, minute, 0);

                        // 指定時刻が現在時刻より前の場合は翌日に設定
                        if (scheduledAt <= now)
                        {
                            scheduledAt = scheduledAt.AddDays(1);
                        }

                        return (scheduledAt, message);
                    }
                }

                // 「yyyy-MM-dd HH:mmに〜」形式
                var fullDateMatch = Regex.Match(input, @"^(\d{4}-\d{2}-\d{2} \d{1,2}:\d{2})に(.+)$");
                if (fullDateMatch.Success)
                {
                    var dateTimeStr = fullDateMatch.Groups[1].Value;
                    var message = fullDateMatch.Groups[2].Value;

                    if (DateTime.TryParse(dateTimeStr, out var scheduledAt))
                    {
                        return (scheduledAt, message);
                    }
                }

                return (null, input);
            }
            catch
            {
                return (null, input);
            }
        }

        #endregion

        #region Bearer Token関連

        /// <summary>
        /// Bearer Token変更イベントハンドラー
        /// </summary>
        private void BearerTokenPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized)
                return;

            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void BearerTokenPasteOverrideButton_Click(object sender, RoutedEventArgs e)
        {
            ClipboardPasteOverride.PasteOverwrite(BearerTokenPasswordBox);
        }

        private void BearerTokenCopyButton_Click(object sender, RoutedEventArgs e)
        {
            ClipboardPasteOverride.CopyToClipboard(BearerTokenPasswordBox);
        }

        /// <summary>
        /// Bearer Tokenを取得
        /// </summary>
        public string GetBearerToken()
        {
            return BearerTokenPasswordBox.Password;
        }

        /// <summary>
        /// Bearer Tokenを設定
        /// </summary>
        public void SetBearerToken(string token)
        {
            BearerTokenPasswordBox.Password = token ?? string.Empty;
        }

        #endregion
    }
}
