using CocoroConsole.Communication;
using CocoroConsole.Models.OtomeKairoApi;
using CocoroConsole.Services;
using CocoroConsole.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        private List<AvatarSettings> _originalAvatarList = new List<AvatarSettings>();

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
                await SystemSettingsControl.InitializeAsync(
                    _apiClient,
                    _communicationService,
                    AppSettings.Instance.ClientId);
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

            _originalAvatarList.Clear();
            foreach (var avatar in AppSettings.Instance.AvatarList)
            {
                _originalAvatarList.Add(DeepCopyAvatarSettings(avatar));
            }
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

                AppSettings.Instance.MicrophoneSettings =
                    SystemSettingsControl.GetMicrophoneSettings().DeepCopy();
                AppSettings.Instance.AudioOutputSettings =
                    SystemSettingsControl.GetAudioOutputSettings().DeepCopy();

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
                _loadedAvatarSpeechEditorState = await _apiClient.ReplaceAvatarSpeechEditorStateAsync(
                    AppSettings.Instance.BuildAvatarSpeechEditorState());
                _loadedConsoleClientEditorState = await _apiClient.ReplaceConsoleClientEditorStateAsync(
                    AppSettings.Instance.ClientId,
                    AppSettings.Instance.BuildConsoleClientSettings());
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

            AppSettings.Instance.AvatarList.Clear();
            foreach (var avatar in _originalAvatarList)
            {
                AppSettings.Instance.AvatarList.Add(DeepCopyAvatarSettings(avatar));
            }

            AvatarManagementControl.RefreshAvatarList();
            SystemSettingsControl.ReloadFromAppSettings();
        }

        private void UpdateAvatarAndAnimationAppSettings()
        {
            var appSettings = AppSettings.Instance;
            AvatarManagementControl.SyncCurrentAvatarFromUi();
            appSettings.CurrentAvatarIndex = AvatarManagementControl.GetCurrentAvatarIndex();
            appSettings.CurrentAnimationSettingIndex = AnimationSettingsControl.GetCurrentAnimationSettingIndex();
            appSettings.AnimationSettings = AnimationSettingsControl.GetAnimationSettings();
        }

        private AvatarSettings DeepCopyAvatarSettings(AvatarSettings source)
        {
            return new AvatarSettings
            {
                avatarId = source.avatarId,
                modelName = source.modelName,
                vrmFilePath = source.vrmFilePath,
                isUseTTS = source.isUseTTS,
                ttsType = source.ttsType,
                voicevoxConfig = new VoicevoxConfig
                {
                    endpointUrl = source.voicevoxConfig.endpointUrl,
                    secondaryEndpointUrl = source.voicevoxConfig.secondaryEndpointUrl,
                    speakerId = source.voicevoxConfig.speakerId,
                    speedScale = source.voicevoxConfig.speedScale,
                    pitchScale = source.voicevoxConfig.pitchScale,
                    intonationScale = source.voicevoxConfig.intonationScale,
                    volumeScale = source.voicevoxConfig.volumeScale,
                    prePhonemeLength = source.voicevoxConfig.prePhonemeLength,
                    postPhonemeLength = source.voicevoxConfig.postPhonemeLength,
                    outputSamplingRate = source.voicevoxConfig.outputSamplingRate,
                    outputStereo = source.voicevoxConfig.outputStereo
                },
                styleBertVits2Config = new StyleBertVits2Config
                {
                    endpointUrl = source.styleBertVits2Config.endpointUrl,
                    modelName = source.styleBertVits2Config.modelName,
                    modelId = source.styleBertVits2Config.modelId,
                    speakerName = source.styleBertVits2Config.speakerName,
                    speakerId = source.styleBertVits2Config.speakerId,
                    style = source.styleBertVits2Config.style,
                    styleWeight = source.styleBertVits2Config.styleWeight,
                    language = source.styleBertVits2Config.language,
                    sdpRatio = source.styleBertVits2Config.sdpRatio,
                    noise = source.styleBertVits2Config.noise,
                    noiseW = source.styleBertVits2Config.noiseW,
                    length = source.styleBertVits2Config.length,
                    autoSplit = source.styleBertVits2Config.autoSplit,
                    splitInterval = source.styleBertVits2Config.splitInterval,
                    assistText = source.styleBertVits2Config.assistText,
                    assistTextWeight = source.styleBertVits2Config.assistTextWeight,
                    referenceAudioPath = source.styleBertVits2Config.referenceAudioPath
                },
                aivisCloudConfig = new AivisCloudConfig
                {
                    apiKey = source.aivisCloudConfig.apiKey,
                    endpointUrl = source.aivisCloudConfig.endpointUrl,
                    modelUuid = source.aivisCloudConfig.modelUuid,
                    speakerUuid = source.aivisCloudConfig.speakerUuid,
                    styleId = source.aivisCloudConfig.styleId,
                    styleName = source.aivisCloudConfig.styleName,
                    useSSML = source.aivisCloudConfig.useSSML,
                    language = source.aivisCloudConfig.language,
                    speakingRate = source.aivisCloudConfig.speakingRate,
                    emotionalIntensity = source.aivisCloudConfig.emotionalIntensity,
                    tempoDynamics = source.aivisCloudConfig.tempoDynamics,
                    pitch = source.aivisCloudConfig.pitch,
                    volume = source.aivisCloudConfig.volume,
                    outputFormat = source.aivisCloudConfig.outputFormat,
                    outputBitrate = source.aivisCloudConfig.outputBitrate,
                    outputSamplingRate = source.aivisCloudConfig.outputSamplingRate,
                    outputAudioChannels = source.aivisCloudConfig.outputAudioChannels
                },
                isUseSTT = source.isUseSTT,
                sttEngine = source.sttEngine,
                sttProfileId = source.sttProfileId,
                sttApiKey = source.sttApiKey,
                isConvertMToon = source.isConvertMToon,
                isEnableShadowOff = source.isEnableShadowOff,
                shadowOffMesh = source.shadowOffMesh,
                isReadOnly = source.isReadOnly
            };
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
