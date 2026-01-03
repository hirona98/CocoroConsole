using CocoroConsole.Communication;
using CocoroConsole.Models.CocoroGhostApi;
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
    /// SettingWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class SettingWindow : Window
    {
        // Display 設定は DisplaySettingsControl に委譲
        private Dictionary<string, object> _originalDisplaySettings = new Dictionary<string, object>();
        private List<CharacterSettings> _originalCharacterList = new List<CharacterSettings>();

        // リマインダー有効/無効の前回値（ローカル設定には保存しない）
        private bool _previousRemindersEnabled = false;

        // 通信サービス
        private ICommunicationService? _communicationService;

        // cocoro_ghost APIクライアント
        private CocoroGhostApiClient? _apiClient;

        // CocoroCore再起動が必要な設定の前回値を保存
        private ConfigSettings _previousCocoroCoreSettings;

        public bool IsClosed { get; private set; } = false;

        public SettingWindow() : this(null)
        {
        }

        public SettingWindow(ICommunicationService? communicationService)
        {
            InitializeComponent();

            _communicationService = communicationService;

            // LLM使用設定（全体設定）を初期表示に反映
            LlmSettingsControl.IsUseLlm = AppSettings.Instance.IsUseLLM;

            // cocoro_ghost APIクライアントを初期化
            InitializeApiClient();

            // Display タブ初期化
            DisplaySettingsControl.SetCommunicationService(_communicationService);
            DisplaySettingsControl.InitializeFromAppSettings();

            // キャラクター設定の初期化
            InitializeCharacterSettings();

            // システム設定コントロールを初期化（APIクライアント設定後に初期化）
            SystemSettingsControl.SetApiClient(_apiClient);
            _ = InitializeSystemSettingsAsync();

            // システム設定変更イベントを登録
            SystemSettingsControl.SettingsChanged += (sender, args) => MarkSettingsChanged();

            // おまけ設定コントロールを初期化
            _ = ExtrasControl.InitializeAsync();

            // おまけ設定変更イベントを登録
            ExtrasControl.SettingsChanged += (sender, args) => MarkSettingsChanged();

            // API説明コントロールを初期化
            _ = ApiDocumentationControl.InitializeAsync();

            // プリセット管理コントロールを初期化
            _ = InitializePresetControlsAsync();

            // 元の設定のバックアップを作成
            BackupSettings();

            // CocoroGhost再起動チェック用に現在の設定のディープコピーを保存
            _previousCocoroCoreSettings = AppSettings.Instance.GetConfigSettings().DeepCopy();
        }

        private async Task InitializeSystemSettingsAsync()
        {
            await SystemSettingsControl.InitializeAsync();
            _previousRemindersEnabled = SystemSettingsControl.GetIsEnableReminder();
        }

        /// <summary>
        /// ウィンドウがロードされた後に呼び出されるイベントハンドラ
        /// </summary>
        protected override void OnSourceInitialized(System.EventArgs e)
        {
            base.OnSourceInitialized(e);
            // Owner設定後にメインサービスを初期化
            InitializeMainServices();
        }

        #region 初期化メソッド

        /// <summary>
        /// メインサービスの初期化
        /// </summary>
        private void InitializeMainServices()
        {
            // 通信サービスの取得（メインウィンドウから）
            if (Owner is MainWindow mainWindow &&
                typeof(MainWindow).GetField("_communicationService", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(mainWindow) is CommunicationService service)
            {
                _communicationService = service;
            }
            LoadLicenseText();
        }

        /// <summary>
        /// cocoro_ghost APIクライアントを初期化
        /// </summary>
        private void InitializeApiClient()
        {
            try
            {
                var appSettings = AppSettings.Instance;
                var baseUrl = $"http://127.0.0.1:{appSettings.CocoroGhostPort}";
                var token = appSettings.CocoroGhostBearerToken;

                if (!string.IsNullOrEmpty(token))
                {
                    _apiClient = new CocoroGhostApiClient(baseUrl, token);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"APIクライアント初期化エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// プリセット管理コントロールを初期化
        /// </summary>
        private async Task InitializePresetControlsAsync()
        {
            if (_apiClient == null) return;

            try
            {
                // APIから設定を取得
                CocoroGhostSettings settings = await _apiClient.GetSettingsAsync();

                // LLM設定をリスト全体でロード
                List<LlmPreset> llmPresets = settings.LlmPreset ?? new List<LlmPreset>();
                LlmSettingsControl.SetApiClient(_apiClient, SaveLlmPresetsToApiAsync);
                LlmSettingsControl.LoadSettingsList(llmPresets, settings.ActiveLlmPresetId);

                // Embedding設定をリスト全体でロード
                List<EmbeddingPreset> embeddingPresets = settings.EmbeddingPreset ?? new List<EmbeddingPreset>();
                EmbeddingSettingsControl.SetApiClient(_apiClient, SaveEmbeddingPresetsToApiAsync);
                EmbeddingSettingsControl.IsMemoryEnabled = settings.MemoryEnabled;
                EmbeddingSettingsControl.LoadSettingsList(embeddingPresets, settings.ActiveEmbeddingPresetId);

                // Promptプリセットをロード
                PromptSettingsControl.SetApiClient(_apiClient, SaveAllSettingsToApiAsync);
                PromptSettingsControl.LoadSettings(
                    settings.PersonaPreset ?? new List<PersonaPreset>(),
                    settings.ActivePersonaPresetId,
                    settings.AddonPreset ?? new List<AddonPreset>(),
                    settings.ActiveAddonPresetId
                );

                // 設定変更イベントを登録
                LlmSettingsControl.SettingsChanged += (sender, args) => MarkSettingsChanged();
                EmbeddingSettingsControl.SettingsChanged += (sender, args) => MarkSettingsChanged();
                PromptSettingsControl.SettingsChanged += (sender, args) => MarkSettingsChanged();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"プリセット管理初期化エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// キャラクター設定の初期化
        /// </summary>
        private void InitializeCharacterSettings()
        {
            // CharacterManagementControlの初期化
            CharacterManagementControl.Initialize();
            CharacterManagementControl.SettingsChanged += (sender, args) => MarkSettingsChanged();

            // キャラクター変更イベントを登録
            CharacterManagementControl.CharacterChanged += (sender, args) =>
            {
                // アニメーション設定を更新
                AnimationSettingsControl.Initialize();
            };

            // アニメーション設定コントロールを初期化
            if (_communicationService != null)
            {
                AnimationSettingsControl.SetCommunicationService(_communicationService);
            }
            AnimationSettingsControl.Initialize();

            // アニメーション設定変更イベントを登録
            AnimationSettingsControl.SettingsChanged += (sender, args) => MarkSettingsChanged();
        }

        // EscapePositionControl は DisplaySettingsControl 内で取り扱う

        /// <summary>
        /// 現在の設定をバックアップする
        /// </summary>
        private void BackupSettings()
        {
            // 表示設定のバックアップ
            DisplaySettingsControl.SaveToSnapshot();
            _originalDisplaySettings = DisplaySettingsControl.GetSnapshot();

            // キャラクターリストのバックアップ（Deep Copy）
            _originalCharacterList.Clear();
            foreach (var character in AppSettings.Instance.CharacterList)
            {
                _originalCharacterList.Add(DeepCopyCharacterSettings(character));
            }
        }

        #endregion

        #region 表示設定メソッド

        private void MarkSettingsChanged()
        {
            if (ApplyButton != null && !ApplyButton.IsEnabled)
            {
                ApplyButton.IsEnabled = true;
            }
        }


        // System やその他設定の収集はこのまま SettingWindow 側で実施
        private Dictionary<string, object> CollectSystemSettings()
        {
            var dict = new Dictionary<string, object>();

            var microphoneSettings = SystemSettingsControl.GetMicrophoneSettings();
            dict["MicInputThreshold"] = microphoneSettings.inputThreshold;
            dict["SpeakerRecognitionThreshold"] = microphoneSettings.speakerRecognitionThreshold;

            // おまけ設定（定期コマンド実行）
            var scheduledCommandSettings = ExtrasControl.GetScheduledCommandSettings();
            dict["ScheduledCommandEnabled"] = scheduledCommandSettings.Enabled;
            dict["ScheduledCommand"] = scheduledCommandSettings.Command;
            dict["ScheduledCommandInterval"] = scheduledCommandSettings.IntervalMinutes;

            // Bearer Token
            dict["BearerToken"] = SystemSettingsControl.GetBearerToken();

            // スクショ除外（ウィンドウタイトル正規表現 / ローカル設定）
            dict["WindowTitleExcludePatterns"] = SystemSettingsControl.GetWindowTitleExcludePatterns();

            return dict;
        }

        #endregion

        /// <summary>
        /// アニメーションチェックボックスのチェック時の処理
        /// </summary>
        private void AnimationCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkBox && checkBox.Tag is AnimationConfig animation)
            {
                animation.isEnabled = true;
            }
        }

        /// <summary>
        /// アニメーションチェックボックスのアンチェック時の処理
        /// </summary>
        private void AnimationCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkBox && checkBox.Tag is AnimationConfig animation)
            {
                animation.isEnabled = false;
            }
        }

        /// <summary>
        /// アニメーション再生ボタンクリック時の処理
        /// </summary>
        private async void PlayAnimationButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is AnimationConfig animation)
            {
                if (_communicationService != null)
                {
                    try
                    {
                        // CocoroShellにアニメーション再生指示を送信
                        await _communicationService.SendAnimationToShellAsync(animation.animationName);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"アニメーション再生エラー: {ex.Message}");
                        UIHelper.ShowError("アニメーション再生エラー", ex.Message);
                    }
                }
                else
                {
                    UIHelper.ShowError("通信エラー", "通信サービスが利用できません。");
                }
            }
        }

        #region 共通ボタンイベントハンドラ
        /// <summary>
        /// OKボタンのクリックイベントハンドラ
        /// </summary>
        private async void OkButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 共通の設定保存処理を実行
                await ApplySettingsChangesAsync();

                // ウィンドウを閉じる
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"設定の保存中にエラーが発生しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// キャンセルボタンのクリックイベントハンドラ
        /// </summary>
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            // 変更を破棄して元の設定に戻す
            RestoreOriginalSettings();

            // ウィンドウを閉じる
            Close();
        }

        /// <summary>
        /// 適用ボタンのクリックイベントハンドラ
        /// </summary>
        private async void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 共通の設定保存処理を実行
                await ApplySettingsChangesAsync();

                // 設定のバックアップを更新（適用後の状態を新しいベースラインとする）
                BackupSettings();
                _previousCocoroCoreSettings = AppSettings.Instance.GetConfigSettings().DeepCopy();
                _previousRemindersEnabled = SystemSettingsControl.GetIsEnableReminder();

                // メインウィンドウのボタン状態とサービスを更新
                UpdateMainWindowStates();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"設定の保存中にエラーが発生しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 設定変更を適用する共通処理
        /// </summary>
        private async Task ApplySettingsChangesAsync()
        {
            var bearerToken = SystemSettingsControl.GetBearerToken();
            if (string.IsNullOrWhiteSpace(bearerToken))
            {
                var result = MessageBox.Show(
                    "cocoro_ghostのBearerトークンが未設定です。チャット/通知/キャプチャは送受信できません。このまま保存しますか？",
                    "Bearerトークン未設定",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (result == MessageBoxResult.No)
                {
                    return;
                }
            }

            // すべてのタブの設定を保存（プリセットの保存・有効化を含む）
            await SaveAllSettingsAsync();

            // 保存後の設定を取得してCocoroGhost再起動が必要かチェック
            var currentSettings = GetCurrentUISettings();
            bool currentRemindersEnabled = SystemSettingsControl.GetIsEnableReminder();
            bool needsCocoroGhostRestart =
                HasCocoroGhostRestartRequiredChanges(_previousCocoroCoreSettings, currentSettings) ||
                currentRemindersEnabled != _previousRemindersEnabled;

            // CocoroShellを再起動
            RestartCocoroShell();

            // CocoroGhostの設定変更があった場合は再起動
            if (needsCocoroGhostRestart)
            {
                await RestartCocoroGhostAsync();
                Debug.WriteLine("CocoroGhost再起動処理を実行しました");
            }

            _previousRemindersEnabled = currentRemindersEnabled;
        }

        /// <summary>
        /// すべてのタブの設定を保存する
        /// </summary>
        private async Task SaveAllSettingsAsync()
        {
            try
            {
                // Display タブのスナップショットを更新
                DisplaySettingsControl.SaveToSnapshot();
                var displaySnapshot = DisplaySettingsControl.GetSnapshot();

                // System の設定を収集
                var systemSnapshot = CollectSystemSettings();

                // AppSettings に反映（Display）
                DisplaySettingsControl.ApplySnapshotToAppSettings(displaySnapshot);

                // AppSettings に反映（System）
                ApplySystemSnapshotToAppSettings(systemSnapshot);

                // Character/Animation の反映
                UpdateCharacterAndAnimationAppSettings();

                // 設定をファイルに保存
                AppSettings.Instance.SaveAppSettings();

                // 全設定をAPIに保存（1回のリクエストで送信）
                await SaveAllSettingsToApiAsync();

                if (_communicationService != null)
                {
                    await _communicationService.RefreshCocoroGhostSettingsAsync();
                }
            }
            catch (System.Exception ex)
            {
                Debug.WriteLine($"[SettingWindow] 設定の保存に失敗しました: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 全設定をAPIに保存（1回のリクエストで送信）
        /// </summary>
        private async Task SaveAllSettingsToApiAsync()
        {
            if (_apiClient == null) return;

            try
            {
                bool memoryEnabled = EmbeddingSettingsControl.IsMemoryEnabled;
                bool desktopWatchEnabled = SystemSettingsControl.GetDesktopWatchEnabled();
                int desktopWatchIntervalSeconds = SystemSettingsControl.GetDesktopWatchIntervalSeconds();
                var desktopWatchTargetClientId = SystemSettingsControl.GetDesktopWatchTargetClientId();
                if (desktopWatchEnabled && string.IsNullOrWhiteSpace(desktopWatchTargetClientId))
                {
                    desktopWatchTargetClientId = AppSettings.Instance.ClientId;
                }
                bool remindersEnabled = SystemSettingsControl.GetIsEnableReminder();
                List<CocoroGhostReminder> reminders = SystemSettingsControl.GetReminders();
                List<LlmPreset> llmPresets = LlmSettingsControl.GetAllPresets();
                List<EmbeddingPreset> embeddingPresets = EmbeddingSettingsControl.GetAllPresets();
                List<PersonaPreset> personaPresets = PromptSettingsControl.GetAllPersonaPresets();
                List<AddonPreset> addonPresets = PromptSettingsControl.GetAllAddonPresets();

                EnsurePresetIds(llmPresets, p => p.LlmPresetId, (p, id) => p.LlmPresetId = id);
                EnsurePresetIds(embeddingPresets, p => p.EmbeddingPresetId, (p, id) => p.EmbeddingPresetId = id);
                EnsurePresetIds(personaPresets, p => p.PersonaPresetId, (p, id) => p.PersonaPresetId = id);
                EnsurePresetIds(addonPresets, p => p.AddonPresetId, (p, id) => p.AddonPresetId = id);

                var activeLlmId = ResolveActivePresetId(llmPresets, LlmSettingsControl.GetActivePresetId(), p => p.LlmPresetId);
                var activeEmbeddingId = ResolveActivePresetId(embeddingPresets, EmbeddingSettingsControl.GetActivePresetId(), p => p.EmbeddingPresetId);
                var activePersonaId = ResolveActivePresetId(personaPresets, PromptSettingsControl.GetActivePersonaPresetId(), p => p.PersonaPresetId);
                var activeAddonId = ResolveActivePresetId(addonPresets, PromptSettingsControl.GetActiveAddonPresetId(), p => p.AddonPresetId);

                if (string.IsNullOrWhiteSpace(activeLlmId) ||
                    string.IsNullOrWhiteSpace(activeEmbeddingId) ||
                    string.IsNullOrWhiteSpace(activePersonaId) ||
                    string.IsNullOrWhiteSpace(activeAddonId))
                {
                    Debug.WriteLine("[SettingWindow] 設定の保存に失敗しました: active preset id missing");
                    MessageBox.Show("アクティブなプリセットが選択されていません。cocoro_ghost側のsettings.dbを確認してください。", "エラー",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                CocoroGhostSettingsUpdateRequest request = new CocoroGhostSettingsUpdateRequest
                {
                    MemoryEnabled = memoryEnabled,
                    DesktopWatchEnabled = desktopWatchEnabled,
                    DesktopWatchIntervalSeconds = desktopWatchIntervalSeconds,
                    DesktopWatchTargetClientId = desktopWatchTargetClientId,
                    RemindersEnabled = remindersEnabled,
                    Reminders = reminders,
                    ActiveLlmPresetId = activeLlmId!,
                    ActiveEmbeddingPresetId = activeEmbeddingId!,
                    ActivePersonaPresetId = activePersonaId!,
                    ActiveAddonPresetId = activeAddonId!,
                    LlmPreset = llmPresets,
                    EmbeddingPreset = embeddingPresets,
                    PersonaPreset = personaPresets,
                    AddonPreset = addonPresets
                };

                CocoroGhostSettings updated = await _apiClient.UpdateSettingsAsync(request);
                Debug.WriteLine("[SettingWindow] 設定をAPIに保存しました");

                List<LlmPreset> updatedLlmPresets = updated.LlmPreset ?? new List<LlmPreset>();
                LlmSettingsControl.LoadSettingsList(updatedLlmPresets, updated.ActiveLlmPresetId);

                List<EmbeddingPreset> updatedEmbeddingPresets = updated.EmbeddingPreset ?? new List<EmbeddingPreset>();
                EmbeddingSettingsControl.IsMemoryEnabled = updated.MemoryEnabled;
                EmbeddingSettingsControl.LoadSettingsList(updatedEmbeddingPresets, updated.ActiveEmbeddingPresetId);

                PromptSettingsControl.LoadSettings(
                    updated.PersonaPreset ?? new List<PersonaPreset>(),
                    updated.ActivePersonaPresetId,
                    updated.AddonPreset ?? new List<AddonPreset>(),
                    updated.ActiveAddonPresetId
                );
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SettingWindow] 設定の保存に失敗しました: {ex.Message}");
                MessageBox.Show($"設定のAPI保存に失敗しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static void EnsurePresetIds<T>(
            IEnumerable<T> presets,
            Func<T, string?> idGetter,
            Action<T, string> idSetter
        )
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var preset in presets)
            {
                var id = idGetter(preset);
                var normalized = id?.Trim();

                if (string.IsNullOrWhiteSpace(normalized) || used.Contains(normalized))
                {
                    normalized = Guid.NewGuid().ToString();
                    idSetter(preset, normalized);
                }
                else if (!string.Equals(id, normalized, StringComparison.Ordinal))
                {
                    idSetter(preset, normalized);
                }

                used.Add(normalized);
            }
        }

        private static string? ResolveActivePresetId<T>(
            IReadOnlyList<T> presets,
            string? activePresetId,
            Func<T, string?> idGetter
        ) where T : class
        {
            if (presets.Count == 0)
            {
                return null;
            }

            var normalizedActiveId = activePresetId?.Trim();
            T? resolved = string.IsNullOrWhiteSpace(normalizedActiveId)
                ? null
                : presets.FirstOrDefault(p => string.Equals(idGetter(p), normalizedActiveId, StringComparison.OrdinalIgnoreCase));

            if (resolved == null || string.IsNullOrWhiteSpace(idGetter(resolved)))
            {
                resolved = presets.FirstOrDefault(p => !string.IsNullOrWhiteSpace(idGetter(p))) ?? presets[0];
            }

            return idGetter(resolved)?.Trim();
        }

        private Task SaveLlmPresetsToApiAsync() => SaveAllSettingsToApiAsync();

        private Task SaveEmbeddingPresetsToApiAsync() => SaveAllSettingsToApiAsync();

        /// <summary>
        /// 元の設定に戻す（一設定などがあるためDisplayのみ復元が必要）
        /// </summary>
        private void RestoreOriginalSettings()
        {
            // Display の復元
            DisplaySettingsControl.ApplySnapshotToAppSettings(_originalDisplaySettings);
            DisplaySettingsControl.InitializeFromAppSettings();

            // キャラクターリストの復元
            AppSettings.Instance.CharacterList.Clear();
            foreach (var character in _originalCharacterList)
            {
                AppSettings.Instance.CharacterList.Add(DeepCopyCharacterSettings(character));
            }

            // CharacterManagementControlのUIを更新
            CharacterManagementControl.RefreshCharacterList();
        }

        #endregion

        #region 設定保存メソッド

        /// <summary>
        /// ウィンドウが閉じられる前に呼び出されるイベントハンドラ
        /// </summary>

        /// <summary>
        /// キャラクター設定のディープコピーを作成
        /// </summary>
        private CharacterSettings DeepCopyCharacterSettings(CharacterSettings source)
        {
            return new CharacterSettings
            {
                modelName = source.modelName,
                vrmFilePath = source.vrmFilePath,
                isUseTTS = source.isUseTTS,
                ttsType = source.ttsType,
                voicevoxConfig = new VoicevoxConfig
                {
                    endpointUrl = source.voicevoxConfig.endpointUrl,
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
                sttWakeWord = source.sttWakeWord,
                sttApiKey = source.sttApiKey,
                sttLanguage = source.sttLanguage,
                isConvertMToon = source.isConvertMToon,
                isEnableShadowOff = source.isEnableShadowOff,
                shadowOffMesh = source.shadowOffMesh,
                isReadOnly = source.isReadOnly
            };
        }

        protected override void OnClosed(EventArgs e)
        {
            IsClosed = true;
            base.OnClosed(e);
        }

        /// <summary>
        /// 表示設定を保存する
        /// </summary>
        // Display タブ以外の設定を AppSettings に適用
        private void ApplySystemSnapshotToAppSettings(Dictionary<string, object> snapshot)
        {
            var appSettings = AppSettings.Instance;

            appSettings.MicrophoneSettings.inputThreshold = (int)snapshot["MicInputThreshold"];
            appSettings.MicrophoneSettings.speakerRecognitionThreshold = (float)snapshot["SpeakerRecognitionThreshold"];

            // おまけ設定（定期コマンド実行）
            appSettings.ScheduledCommandSettings.Enabled = (bool)snapshot["ScheduledCommandEnabled"];
            appSettings.ScheduledCommandSettings.Command = (string)snapshot["ScheduledCommand"];
            appSettings.ScheduledCommandSettings.IntervalMinutes = (int)snapshot["ScheduledCommandInterval"];

            // Bearer Token
            appSettings.CocoroGhostBearerToken = (string)snapshot["BearerToken"];

            // スクショ除外（ウィンドウタイトル正規表現 / ローカル設定）
            appSettings.ScreenshotSettings.excludePatterns = (List<string>)snapshot["WindowTitleExcludePatterns"];
        }

        /// <summary>
        /// AppSettingsを更新する
        /// </summary>
        private void UpdateCharacterAndAnimationAppSettings()
        {
            var appSettings = AppSettings.Instance;
            appSettings.CurrentCharacterIndex = CharacterManagementControl.GetCurrentCharacterIndex();
            appSettings.IsUseLLM = LlmSettingsControl.IsUseLlm;

            var currentCharacterSetting = CharacterManagementControl.GetCurrentCharacterSettingFromUI();
            if (currentCharacterSetting != null)
            {
                var currentIndex = CharacterManagementControl.GetCurrentCharacterIndex();
                if (currentIndex >= 0 &&
                    currentIndex < appSettings.CharacterList.Count)
                {
                    // 現在のキャラクターの設定を更新
                    appSettings.CharacterList[currentIndex] = currentCharacterSetting;
                    // 注: LLM/Embedding設定はAPI経由で管理される
                }
            }

            appSettings.CurrentAnimationSettingIndex = AnimationSettingsControl.GetCurrentAnimationSettingIndex();
            appSettings.AnimationSettings = AnimationSettingsControl.GetAnimationSettings();
        }

        #endregion

        #region VRMファイル選択イベントハンドラ

        /// <summary>
        /// VRMファイル参照ボタンのクリックイベント
        /// </summary>
        private void BrowseVrmFileButton_Click(object sender, RoutedEventArgs e)
        {
            // ファイルダイアログの設定
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "VRMファイルを選択",
                Filter = "VRMファイル (*.vrm)|*.vrm|すべてのファイル (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false
            };
        }

        #endregion

        private void LoadLicenseText()
        {
            try
            {
                // 埋め込みリソースからライセンステキストを読み込む
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                var resourceName = "CocoroConsole.Resource.License.txt";

                using (var stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream != null)
                    {
                        using (var reader = new System.IO.StreamReader(stream))
                        {
                            string licenseText = reader.ReadToEnd();
                            LicenseTextBox.Text = licenseText;
                        }
                    }
                    else
                    {
                        // リソースが見つからない場合
                        LicenseTextBox.Text = "ライセンスリソースが見つかりませんでした。";
                    }
                }
            }
            catch (Exception ex)
            {
                // エラーが発生した場合
                LicenseTextBox.Text = $"ライセンスリソースの読み込み中にエラーが発生しました: {ex.Message}";
            }
        }

        /// <summary>
        /// ハイパーリンクをクリックしたときにブラウザで開く
        /// </summary>
        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = e.Uri.AbsoluteUri,
                    UseShellExecute = true
                });
                e.Handled = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"URLを開けませんでした: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// メインウィンドウのボタン状態とサービスを更新
        /// </summary>
        private void UpdateMainWindowStates()
        {
            try
            {
                // MainWindowのインスタンスを取得
                var mainWindow = Application.Current.MainWindow as MainWindow;
                if (mainWindow != null)
                {
                    // InitializeButtonStatesメソッドを呼び出してボタン状態を更新
                    var initButtonMethod = mainWindow.GetType().GetMethod("InitializeButtonStates",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                    if (initButtonMethod != null)
                    {
                        initButtonMethod.Invoke(mainWindow, null);
                        Debug.WriteLine("[SettingWindow] メインウィンドウのボタン状態を更新しました");
                    }

                    // ApplySettingsメソッドを呼び出してサービスを更新
                    var applyMethod = mainWindow.GetType().GetMethod("ApplySettings",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                    if (applyMethod != null)
                    {
                        applyMethod.Invoke(mainWindow, null);
                        Debug.WriteLine("[SettingWindow] メインウィンドウのサービスを更新しました");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SettingWindow] メインウィンドウの状態更新中にエラーが発生しました: {ex.Message}");
            }
        }

        /// <summary>
        /// ログ表示ボタンのクリックイベント
        /// </summary>
        private void LogViewerButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 通信サービスからログビューアーを開く
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

        /// <summary>
        /// CocoroGhostを再起動する
        /// </summary>
        private async Task RestartCocoroGhostAsync()
        {
            try
            {
                // MainWindowのインスタンスを取得
                var mainWindow = Application.Current.MainWindow as MainWindow;
                if (mainWindow != null)
                {
                    // ProcessOperation.RestartIfRunning を指定してCocoroGhostを再起動（非同期）
                    await mainWindow.LaunchCocoroGhostAsync(ProcessOperation.RestartIfRunning);
                    Debug.WriteLine("CocoroGhostを再起動要求をしました");

                    // 再起動完了を待機
                    await WaitForCocoroGhostRestartAsync();
                    Debug.WriteLine("CocoroGhostの再起動が完了しました");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"CocoroGhost再起動中にエラーが発生しました: {ex.Message}");
                throw new Exception($"CocoroGhostの再起動に失敗しました: {ex.Message}");
            }
        }

        /// <summary>
        /// CocoroGhostの再起動完了を待機
        /// </summary>
        private async Task WaitForCocoroGhostRestartAsync()
        {
            var delay = TimeSpan.FromSeconds(1);
            var maxWaitTime = TimeSpan.FromSeconds(120);
            var startTime = DateTime.Now;

            bool hasBeenDisconnected = false;

            while (DateTime.Now - startTime < maxWaitTime)
            {
                try
                {
                    if (_communicationService != null)
                    {
                        var currentStatus = _communicationService.CurrentStatus;

                        // まず停止（起動待ち）状態になることを確認
                        if (!hasBeenDisconnected)
                        {
                            if (currentStatus == CocoroGhostStatus.WaitingForStartup)
                            {
                                hasBeenDisconnected = true;
                                Debug.WriteLine("CocoroGhost停止を確認（起動待ち）");
                            }
                        }
                        // 停止を確認済みの場合、再起動完了を待機
                        else
                        {
                            if (currentStatus == CocoroGhostStatus.Normal ||
                                currentStatus == CocoroGhostStatus.ProcessingMessage ||
                                currentStatus == CocoroGhostStatus.ProcessingImage)
                            {
                                Debug.WriteLine("CocoroGhost再起動完了");
                                return;
                            }
                        }
                    }
                }
                catch
                {
                    // API未応答時は継続してチェック
                }
                await Task.Delay(delay);
            }

            throw new TimeoutException("CocoroGhostの再起動がタイムアウトしました");
        }

        /// <summary>
        /// UI上の現在の設定を取得する（ディープコピー）
        /// </summary>
        /// <returns>現在のUI設定から構築したConfigSettings</returns>
        private ConfigSettings GetCurrentUISettings()
        {
            // 現在の設定のディープコピーを作成
            var config = AppSettings.Instance.GetConfigSettings().DeepCopy();

            // LLM使用設定
            config.isUseLLM = LlmSettingsControl.IsUseLlm;

            // Character設定の取得（ディープコピーを使用）
            config.currentCharacterIndex = CharacterManagementControl.GetCurrentCharacterIndex();
            var currentCharacterSetting = CharacterManagementControl.GetCurrentCharacterSettingFromUI();
            if (currentCharacterSetting != null)
            {
                if (config.currentCharacterIndex >= 0 && config.currentCharacterIndex < config.characterList.Count)
                {
                    config.characterList[config.currentCharacterIndex] = currentCharacterSetting;
                }
            }

            return config;
        }

        /// <summary>
        /// CocoroGhost再起動が必要な設定項目が変更されたかどうかをチェック
        /// </summary>
        /// <param name="previousSettings">以前の設定</param>
        /// <param name="currentSettings">現在の設定</param>
        /// <returns>CocoroGhost再起動が必要な変更があった場合true</returns>
        private bool HasCocoroGhostRestartRequiredChanges(ConfigSettings previousSettings, ConfigSettings currentSettings)
        {
            // 基本設定項目の比較
            if (currentSettings.currentCharacterIndex != previousSettings.currentCharacterIndex)
            {
                return true;
            }

            if (currentSettings.isUseLLM != previousSettings.isUseLLM)
            {
                return true;
            }

            // キャラクターリストの比較
            if (currentSettings.characterList.Count != previousSettings.characterList.Count)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// CocoroShellを再起動する
        /// </summary>
        private void RestartCocoroShell()
        {
            try
            {
                // MainWindowのインスタンスを取得
                var mainWindow = Application.Current.MainWindow as MainWindow;
                if (mainWindow != null)
                {
                    // MainWindowのLaunchCocoroShellメソッドを呼び出してCocoroShellを再起動
                    var launchMethod = mainWindow.GetType().GetMethod("LaunchCocoroShell",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                    if (launchMethod != null)
                    {
                        // ProcessOperation.RestartIfRunning を指定してCocoroShellを再起動
                        launchMethod.Invoke(mainWindow, [ProcessOperation.RestartIfRunning]);
                        Debug.WriteLine("CocoroShellを再起動しました");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"CocoroShell再起動中にエラーが発生しました: {ex.Message}");
                MessageBox.Show($"CocoroShellの再起動に失敗しました: {ex.Message}",
                               "警告",
                               MessageBoxButton.OK,
                               MessageBoxImage.Warning);
            }
        }
    }
}
