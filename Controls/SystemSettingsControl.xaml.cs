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

                // デスクトップウォッチ設定
                ScreenshotEnabledCheckBox.IsChecked = appSettings.ScreenshotSettings.enabled;
                CaptureActiveWindowOnlyCheckBox.IsChecked = appSettings.ScreenshotSettings.captureActiveWindowOnly;
                ScreenshotIntervalTextBox.Text = appSettings.ScreenshotSettings.intervalMinutes.ToString();
                IdleTimeoutTextBox.Text = appSettings.ScreenshotSettings.idleTimeoutMinutes.ToString();

                // /api/settings から設定を読み込み（API利用可能な場合）
                await LoadSettingsFromApiAsync(appSettings);

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

        #region /api/settings API連携

        /// <summary>
        /// APIから設定を読み込み（exclude_keywords / reminders_enabled / reminders）
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
                        _apiExcludeKeywords = settings.ExcludeKeywords ?? new List<string>();
                        _apiLlmPresets = settings.LlmPreset ?? new List<LlmPreset>();
                        _apiEmbeddingPresets = settings.EmbeddingPreset ?? new List<EmbeddingPreset>();
                        _apiReminders = settings.Reminders ?? new List<CocoroGhostReminder>();
                        EnableReminderCheckBox.IsChecked = settings.RemindersEnabled;
                        ExcludePatternsTextBox.Text = string.Join(Environment.NewLine, _apiExcludeKeywords);
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
            ExcludePatternsTextBox.Text = string.Join(Environment.NewLine, appSettings.ScreenshotSettings.excludePatterns);
            EnableReminderCheckBox.IsChecked = false;
            _apiReminders = new List<CocoroGhostReminder>();
            UpdateReminderListUI();
        }

        /// <summary>
        /// exclude_keywordsをAPIに保存（/api/settings は全量PUTのため他項目も保全して送信）
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

                // 最新設定を取得し、他項目を保全したまま更新する
                var latestSettings = await _apiClient.GetSettingsAsync();
                latestSettings ??= new CocoroGhostSettings();

                var activeLlmId = latestSettings.ActiveLlmPresetId ?? latestSettings.LlmPreset.FirstOrDefault()?.LlmPresetId;
                var activeEmbeddingId = latestSettings.ActiveEmbeddingPresetId ?? latestSettings.EmbeddingPreset.FirstOrDefault()?.EmbeddingPresetId;
                var activePersonaId = latestSettings.ActivePersonaPresetId ?? latestSettings.PersonaPreset.FirstOrDefault()?.PersonaPresetId;
                var activeAddonId = latestSettings.ActiveAddonPresetId ?? latestSettings.AddonPreset.FirstOrDefault()?.AddonPresetId;

                if (string.IsNullOrWhiteSpace(activeLlmId) ||
                    string.IsNullOrWhiteSpace(activeEmbeddingId) ||
                    string.IsNullOrWhiteSpace(activePersonaId) ||
                    string.IsNullOrWhiteSpace(activeAddonId))
                {
                    MessageBox.Show("API設定のアクティブプリセットIDが取得できません。cocoro_ghost側のsettings.dbを確認してください。", "警告",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }

                var request = new CocoroGhostSettingsUpdateRequest
                {
                    ExcludeKeywords = patterns,
                    RemindersEnabled = latestSettings.RemindersEnabled,
                    Reminders = latestSettings.Reminders ?? new List<CocoroGhostReminder>(),
                    ActiveLlmPresetId = activeLlmId!,
                    ActiveEmbeddingPresetId = activeEmbeddingId!,
                    ActivePersonaPresetId = activePersonaId!,
                    ActiveAddonPresetId = activeAddonId!,
                    LlmPreset = latestSettings.LlmPreset ?? new List<LlmPreset>(),
                    EmbeddingPreset = latestSettings.EmbeddingPreset ?? new List<EmbeddingPreset>(),
                    PersonaPreset = latestSettings.PersonaPreset ?? new List<PersonaPreset>(),
                    AddonPreset = latestSettings.AddonPreset ?? new List<AddonPreset>()
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
        /// 現在時刻設定ボタンクリック
        /// </summary>
        private void SetCurrentTimeButton_Click(object sender, RoutedEventArgs e)
        {
            ReminderDateTimeTextBox.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
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
