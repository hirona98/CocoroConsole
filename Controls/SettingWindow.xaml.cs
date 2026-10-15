using CocoroConsole.Models.OtomeKairoApi;
using CocoroConsole.Services;
using CocoroConsole.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace CocoroConsole.Controls
{
    /// <summary>
    /// 端末固有設定とアバター・マイク設定を編集する設定ウィンドウ。
    /// 人格・モデル・記憶・定期思考・カメラ・Watcher・MCP は OtomeKairo WebUI で編集する。
    /// </summary>
    public partial class SettingWindow : Window
    {
        private Dictionary<string, object> _originalDisplaySettings = new Dictionary<string, object>();
        private OtomeKairoAvatarSpeechEditorState _originalAvatarSpeech = new OtomeKairoAvatarSpeechEditorState();
        private List<OtomeKairoConsoleAvatarPresentation> _originalAvatarPresentations = new List<OtomeKairoConsoleAvatarPresentation>();
        private OtomeKairoConsoleMotionSettings _originalMotion = new OtomeKairoConsoleMotionSettings();

        private ICommunicationService? _communicationService;
        private OtomeKairoApiClient? _apiClient;

        private OtomeKairoConsoleClientEditorState? _loadedConsoleClientEditorState;
        private OtomeKairoAvatarSpeechEditorState? _loadedAvatarSpeechEditorState;

        public bool IsClosed { get; private set; } = false;

        public SettingWindow() : this(null)
        {
        }

        public SettingWindow(ICommunicationService? communicationService)
        {
            InitializeComponent();
            ShowSettingsPage("display");

            _communicationService = communicationService;

            InitializeApiClient();

            DisplaySettingsControl.SetCommunicationService(_communicationService);
            DisplaySettingsControl.InitializeFromAppSettings();

            InitializeAvatarSettings();
            SystemSettingsControl.SettingsChanged += (sender, args) => MarkSettingsChanged();
            SystemSettingsControl.Initialize();
            _ = InitializeRemoteSettingsAsync();

            BackupSettings();
        }

        protected override void OnSourceInitialized(System.EventArgs e)
        {
            base.OnSourceInitialized(e);
            InitializeMainServices();
        }

        private void InitializeMainServices()
        {
            if (Owner is MainWindow mainWindow &&
                typeof(MainWindow).GetField("_communicationService", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(mainWindow) is CommunicationService service)
            {
                _communicationService = service;
            }
            LoadLicenseText();
        }

        private void InitializeApiClient()
        {
            try
            {
                _apiClient?.Dispose();
                _apiClient = null;

                var appSettings = AppSettings.Instance;
                var baseUrl = appSettings.GetOtomeKairoBaseUrl();
                var token = appSettings.OtomeKairoBearerToken;

                if (!string.IsNullOrEmpty(token))
                {
                    _apiClient = new OtomeKairoApiClient(baseUrl, token);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"APIクライアント初期化エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// 端末設定とアバター音声設定を取得し、表示・アバター・マイク UI に反映する。
        /// </summary>
        private async Task InitializeRemoteSettingsAsync()
        {
            if (_apiClient == null)
            {
                return;
            }

            try
            {
                _loadedConsoleClientEditorState = await _apiClient.ConnectConsoleClientAsync(
                    AppSettings.Instance.ClientId);
                _loadedAvatarSpeechEditorState = await _apiClient.GetAvatarSpeechEditorStateAsync();
                var currentSettings = (await _apiClient.GetOtomeKairoConfigAsync()).SettingsSnapshot;
                AppSettings.Instance.ApplyRemoteSettings(
                    _loadedConsoleClientEditorState.Settings,
                    currentSettings,
                    (await _apiClient.GetConversationDisplayNamesAsync()).ConversationDisplayNames,
                    _loadedAvatarSpeechEditorState);

                DisplaySettingsControl.InitializeFromAppSettings();
                AvatarManagementControl.RefreshAvatarList();
                AnimationSettingsControl.Initialize();
                SystemSettingsControl.ReloadFromAppSettings();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"端末設定初期化エラー: {ex.Message}");
            }
        }

        private void InitializeAvatarSettings()
        {
            AvatarManagementControl.Initialize();
            AvatarManagementControl.SettingsChanged += (sender, args) => MarkSettingsChanged();

            AvatarManagementControl.AvatarChanged += (sender, args) =>
            {
                AnimationSettingsControl.Initialize();
            };

            if (_communicationService != null)
            {
                AnimationSettingsControl.SetCommunicationService(_communicationService);
            }
            AnimationSettingsControl.Initialize();
            AnimationSettingsControl.SettingsChanged += (sender, args) => MarkSettingsChanged();
        }

        private void BackupSettings()
        {
            DisplaySettingsControl.SaveToSnapshot();
            _originalDisplaySettings = DisplaySettingsControl.GetSnapshot();

            var appSettings = AppSettings.Instance;
            _originalAvatarSpeech = AppSettings.CloneDocument(appSettings.AvatarSpeech);
            _originalAvatarPresentations = appSettings.ConsoleClient.AvatarPresentations
                .Select(AppSettings.CloneDocument)
                .ToList();
            _originalMotion = AppSettings.CloneDocument(appSettings.ConsoleClient.Motion);
        }

        private void MarkSettingsChanged()
        {
            if (ApplyButton != null && !ApplyButton.IsEnabled)
            {
                ApplyButton.IsEnabled = true;
            }
        }

        private void NavigationButton_Checked(object sender, RoutedEventArgs e)
        {
            if (SettingsContentHost == null
                || sender is not RadioButton { Tag: string pageId })
            {
                return;
            }

            ShowSettingsPage(pageId);
        }

        private void ShowSettingsPage(string pageId)
        {
            DisplaySettingsControl.Visibility = Visibility.Collapsed;
            AvatarManagementControl.Visibility = Visibility.Collapsed;
            AnimationSettingsControl.Visibility = Visibility.Collapsed;
            SystemSettingsControl.Visibility = Visibility.Collapsed;
            LicensePage.Visibility = Visibility.Collapsed;

            switch (pageId)
            {
                case "avatar":
                    ShowPage(AvatarManagementControl);
                    break;
                case "motion":
                    ShowPage(AnimationSettingsControl);
                    break;
                case "microphone":
                    ShowPage(SystemSettingsControl);
                    break;
                case "license":
                    ShowPage(LicensePage);
                    break;
                default:
                    ShowPage(DisplaySettingsControl);
                    break;
            }
        }

        private void ShowPage(FrameworkElement page)
        {
            page.Visibility = Visibility.Visible;
        }

        private async void OkButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await ApplySettingsChangesAsync();
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"設定の保存中にエラーが発生しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            RestoreOriginalSettings();
            Close();
        }

        private async void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await ApplySettingsChangesAsync();
                BackupSettings();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"設定の保存中にエラーが発生しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task ApplySettingsChangesAsync()
        {
            if (!AreRemoteSettingsLoaded())
            {
                throw new InvalidOperationException(
                    "OtomeKairoの通常設定を取得していません。トレイの「接続先設定」で接続し直してください。");
            }

            await SaveAllSettingsAsync();
        }

        private bool AreRemoteSettingsLoaded()
        {
            return _loadedConsoleClientEditorState != null &&
                _loadedAvatarSpeechEditorState != null;
        }

        private async Task SaveAllSettingsAsync()
        {
            try
            {
                DisplaySettingsControl.SaveToSnapshot();
                var displaySnapshot = DisplaySettingsControl.GetSnapshot();
                DisplaySettingsControl.ApplySnapshotToAppSettings(displaySnapshot);

                SystemSettingsControl.ApplyToAppSettings();
                UpdateAvatarAndAnimationAppSettings();
                AppSettings.Instance.SaveAppSettings();

                await SaveAllSettingsToApiAsync();

                if (_communicationService != null)
                {
                    await _communicationService.RefreshOtomeKairoCurrentSettingsAsync();
                }
            }
            catch (System.Exception ex)
            {
                Debug.WriteLine($"[SettingWindow] 設定の保存に失敗しました: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 端末設定とアバター音声設定だけを API へ保存する。
        /// </summary>
        private async Task SaveAllSettingsToApiAsync()
        {
            if (_apiClient == null)
            {
                throw new InvalidOperationException("OtomeKairo APIクライアントを初期化できません。");
            }

            try
            {
                AppSettings.Instance.PrepareAvatarSpeechForSave();
                AppSettings.Instance.PrepareConsoleClientForSave();
                _loadedAvatarSpeechEditorState = await _apiClient.ReplaceAvatarSpeechEditorStateAsync(
                    AppSettings.Instance.AvatarSpeech);
                _loadedConsoleClientEditorState = await _apiClient.ReplaceConsoleClientEditorStateAsync(
                    AppSettings.Instance.ClientId,
                    AppSettings.Instance.ConsoleClient);
                Debug.WriteLine("[SettingWindow] 端末設定とアバター音声設定を OtomeKairo API に保存しました");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SettingWindow] 設定の保存に失敗しました: {ex.Message}");
                throw;
            }
        }

        private void RestoreOriginalSettings()
        {
            DisplaySettingsControl.ApplySnapshotToAppSettings(_originalDisplaySettings);
            DisplaySettingsControl.InitializeFromAppSettings();

            var appSettings = AppSettings.Instance;
            appSettings.AvatarSpeech = AppSettings.CloneDocument(_originalAvatarSpeech);
            appSettings.ConsoleClient.AvatarPresentations = _originalAvatarPresentations
                .Select(AppSettings.CloneDocument)
                .ToList();
            appSettings.ConsoleClient.Motion = AppSettings.CloneDocument(_originalMotion);

            AvatarManagementControl.RefreshAvatarList();
            AnimationSettingsControl.Initialize();
            SystemSettingsControl.ReloadFromAppSettings();
        }

        private void UpdateAvatarAndAnimationAppSettings()
        {
            var motion = AppSettings.Instance.ConsoleClient.Motion;
            AvatarManagementControl.SyncCurrentAvatarFromUi();
            var animationSets = AnimationSettingsControl.GetAnimationSettings();
            motion.AnimationSets = animationSets;
            var index = AnimationSettingsControl.GetCurrentAnimationSettingIndex();
            if (index >= 0 && index < animationSets.Count)
            {
                motion.SelectedAnimationSetId = animationSets[index].AnimationSetId;
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            IsClosed = true;
            _apiClient?.Dispose();
            _apiClient = null;
            _communicationService = null;
            base.OnClosed(e);
        }

        private void LoadLicenseText()
        {
            try
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                var resourceName = "CocoroConsole.Resource.License.txt";

                using (var stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream != null)
                    {
                        using (var reader = new System.IO.StreamReader(stream))
                        {
                            LicenseTextBox.Text = reader.ReadToEnd();
                        }
                    }
                    else
                    {
                        LicenseTextBox.Text = "ライセンスリソースが見つかりませんでした。";
                    }
                }
            }
            catch (Exception ex)
            {
                LicenseTextBox.Text = $"ライセンスリソースの読み込み中にエラーが発生しました: {ex.Message}";
            }
        }

        private void LogViewerButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _communicationService?.OpenLogViewer();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"ログビューアーの起動に失敗しました: {ex.Message}",
                               "エラー",
                               MessageBoxButton.OK,
                               MessageBoxImage.Error);
            }
        }
    }
}
