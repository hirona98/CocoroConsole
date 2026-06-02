using CocoroConsole.Communication;
using CocoroConsole.Models.OtomeKairoApi;
using CocoroConsole.Services;
using CocoroConsole.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
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

        // 通信サービス
        private ICommunicationService? _communicationService;

        // otomekairo APIクライアント
        private OtomeKairoApiClient? _apiClient;

        // OtomeKairo の editor-state を保持
        private OtomeKairoEditorState? _loadedOtomeKairoEditorState;

        // OtomeKairo再起動が必要な設定の前回値を保存
        private ConfigSettings _previousOtomeKairoSettings;

        public bool IsClosed { get; private set; } = false;

        public SettingWindow() : this(null)
        {
        }

        public SettingWindow(ICommunicationService? communicationService)
        {
            InitializeComponent();
            EmbeddingSettingsControl.ResolveLlmApiKey = () => LlmSettingsControl.GetPreferredApiKeyForEmbeddingPaste();

            _communicationService = communicationService;

            // LLM使用設定（全体設定）を初期表示に反映
            LlmSettingsControl.IsUseLlm = AppSettings.Instance.IsUseLLM;

            // otomekairo APIクライアントを初期化
            InitializeApiClient();

            // Display タブ初期化
            DisplaySettingsControl.SetCommunicationService(_communicationService);
            DisplaySettingsControl.InitializeFromAppSettings();

            // アバター設定の初期化
            InitializeCharacterSettings();

            // システム設定コントロールを初期化（APIクライアント設定後に初期化）
            _ = InitializeSystemSettingsAsync();

            // システム設定変更イベントを登録
            SystemSettingsControl.SettingsChanged += (sender, args) => MarkSettingsChanged();

            // API説明コントロールを初期化
            _ = ApiDocumentationControl.InitializeAsync();

            // プリセット管理コントロールを初期化
            _ = InitializePresetControlsAsync();

            // 元の設定のバックアップを作成
            BackupSettings();

            // OtomeKairo再起動チェック用に現在の設定のディープコピーを保存
            _previousOtomeKairoSettings = AppSettings.Instance.GetConfigSettings().DeepCopy();
        }

        private async Task InitializeSystemSettingsAsync()
        {
            await SystemSettingsControl.InitializeAsync();
        }

        public void SetWakeDesktopObservationEnabled(bool enabled)
        {
            SystemSettingsControl.SetWakeDesktopObservationEnabled(enabled);
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
        /// otomekairo APIクライアントを初期化
        /// </summary>
        private void InitializeApiClient()
        {
            try
            {
                // --- 既存クライアントを破棄して現在の設定で作り直す ---
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
        /// プリセット管理コントロールを初期化
        /// </summary>
        private async Task InitializePresetControlsAsync()
        {
            if (_apiClient == null) return;

            try
            {
                _loadedOtomeKairoEditorState = await _apiClient.GetEditorStateAsync();

                LlmSettingsControl.LoadSettingsList(
                    CloneModelPresets(_loadedOtomeKairoEditorState.ModelPresets),
                    _loadedOtomeKairoEditorState.Current.SelectedModelPresetId
                );

                EmbeddingSettingsControl.LoadSettings(
                    CloneMemorySets(_loadedOtomeKairoEditorState.MemorySets),
                    _loadedOtomeKairoEditorState.Current.SelectedMemorySetId
                );

                PromptSettingsControl.LoadSettings(
                    ClonePersonas(_loadedOtomeKairoEditorState.Personas),
                    _loadedOtomeKairoEditorState.Current.SelectedPersonaId
                );

                SystemSettingsControl.ApplyOtomeKairoCurrentSettings(_loadedOtomeKairoEditorState.Current);

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
        /// アバター設定の初期化
        /// </summary>
        private void InitializeCharacterSettings()
        {
            // CharacterManagementControlの初期化
            CharacterManagementControl.Initialize();
            CharacterManagementControl.SettingsChanged += (sender, args) => MarkSettingsChanged();

            // アバター変更イベントを登録
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

            // アバターリストのバックアップ（Deep Copy）
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

            // スクショ除外（ウィンドウタイトル正規表現 / ローカル設定）
            dict["WindowTitleExcludePatterns"] = SystemSettingsControl.GetWindowTitleExcludePatterns();

            // 視覚キャプチャ（アイドルタイムアウト / ローカル設定）
            dict["VisualCaptureIdleTimeoutMinutes"] = SystemSettingsControl.GetVisualCaptureIdleTimeoutMinutes();

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
                _previousOtomeKairoSettings = AppSettings.Instance.GetConfigSettings().DeepCopy();
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
            // OtomeKairo 接続情報は専用ウィンドウで管理し、ここでは現在の保存値を参照する。
            var bearerToken = AppSettings.Instance.OtomeKairoBearerToken;
            if (string.IsNullOrWhiteSpace(bearerToken))
            {
                var result = MessageBox.Show(
                    "otomekairoのBearerトークンが未設定です。チャット/通知/キャプチャは送受信できません。このまま保存しますか？",
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

            // 保存後の設定を取得してOtomeKairo再起動が必要かチェック
            var currentSettings = GetCurrentUISettings();
            bool needsOtomeKairoRestart =
                HasOtomeKairoRestartRequiredChanges(_previousOtomeKairoSettings, currentSettings);

            // CocoroShellを再起動
            RestartCocoroShell();

            // OtomeKairoの設定変更があった場合は再起動
            if (needsOtomeKairoRestart)
            {
                await RestartOtomeKairoAsync();
                Debug.WriteLine("OtomeKairo再起動処理を実行しました");
            }
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
        /// 全設定をAPIに保存（1回のリクエストで送信）
        /// </summary>
        private async Task SaveAllSettingsToApiAsync()
        {
            InitializeApiClient();
            if (_apiClient == null) return;

            try
            {
                _loadedOtomeKairoEditorState ??= await _apiClient.GetEditorStateAsync();
                await SyncMemorySetsAsync(_loadedOtomeKairoEditorState);

                var request = BuildEditorStateFromUi();
                var updated = await _apiClient.ReplaceEditorStateAsync(request);
                _loadedOtomeKairoEditorState = updated;
                Debug.WriteLine("[SettingWindow] editor-state を API に保存しました");

                LlmSettingsControl.LoadSettingsList(
                    CloneModelPresets(updated.ModelPresets),
                    updated.Current.SelectedModelPresetId
                );

                EmbeddingSettingsControl.LoadSettings(
                    CloneMemorySets(updated.MemorySets),
                    updated.Current.SelectedMemorySetId
                );

                PromptSettingsControl.LoadSettings(
                    ClonePersonas(updated.Personas),
                    updated.Current.SelectedPersonaId
                );

                SystemSettingsControl.ApplyOtomeKairoCurrentSettings(updated.Current);
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

        private async Task SyncMemorySetsAsync(OtomeKairoEditorState baseState)
        {
            if (_apiClient == null)
            {
                return;
            }

            var memorySets = EmbeddingSettingsControl.GetAllMemorySets();
            EnsurePresetIds(memorySets, p => p.MemorySetId, (p, id) => p.MemorySetId = id);

            var activeMemorySetId = ResolveActivePresetId(
                memorySets,
                EmbeddingSettingsControl.GetActiveMemorySetId(),
                p => p.MemorySetId);
            if (string.IsNullOrWhiteSpace(activeMemorySetId))
            {
                throw new InvalidOperationException("アクティブな記憶集合を解決できませんでした。");
            }

            var baseMemorySets = baseState.MemorySets.ToDictionary(
                memorySet => memorySet.MemorySetId,
                StringComparer.OrdinalIgnoreCase);
            var pendingClones = EmbeddingSettingsControl.GetPendingCloneRequests();
            var pendingCloneIds = new HashSet<string>(
                pendingClones.Select(item => item.Definition.MemorySetId),
                StringComparer.OrdinalIgnoreCase);
            var desiredMemorySetIds = new HashSet<string>(
                memorySets.Select(memorySet => memorySet.MemorySetId),
                StringComparer.OrdinalIgnoreCase);

            foreach (var pendingClone in pendingClones)
            {
                await _apiClient.CloneMemorySetAsync(
                    pendingClone.SourceMemorySetId,
                    pendingClone.Definition.MemorySetId,
                    pendingClone.Definition.DisplayName);
            }

            foreach (var memorySet in memorySets.Where(memorySet =>
                         !baseMemorySets.ContainsKey(memorySet.MemorySetId)
                         && !pendingCloneIds.Contains(memorySet.MemorySetId)))
            {
                await _apiClient.ReplaceMemorySetAsync(CloneMemorySet(memorySet));
            }

            foreach (var memorySet in memorySets.Where(memorySet =>
                         baseMemorySets.TryGetValue(memorySet.MemorySetId, out var existing)
                         && MemorySetDefinitionChanged(existing, memorySet)))
            {
                await _apiClient.ReplaceMemorySetAsync(CloneMemorySet(memorySet));
            }

            var deletedMemorySetIds = baseMemorySets.Keys
                .Where(memorySetId => !desiredMemorySetIds.Contains(memorySetId))
                .ToList();
            if (deletedMemorySetIds.Count == 0)
            {
                return;
            }

            if (deletedMemorySetIds.Any(memorySetId =>
                    string.Equals(memorySetId, baseState.Current.SelectedMemorySetId, StringComparison.OrdinalIgnoreCase)))
            {
                await _apiClient.PatchCurrentConfigAsync(new OtomeKairoCurrentSettingsPatch
                {
                    SelectedMemorySetId = activeMemorySetId,
                });
            }

            foreach (var memorySetId in deletedMemorySetIds)
            {
                await _apiClient.DeleteMemorySetAsync(memorySetId);
            }
        }

        private OtomeKairoEditorState BuildEditorStateFromUi()
        {
            var personas = PromptSettingsControl.GetAllPersonas();
            var memorySets = EmbeddingSettingsControl.GetAllMemorySets();
            var modelPresets = LlmSettingsControl.GetAllPresets();

            EnsurePresetIds(personas, p => p.PersonaId, (p, id) => p.PersonaId = id);
            EnsurePresetIds(memorySets, p => p.MemorySetId, (p, id) => p.MemorySetId = id);
            EnsurePresetIds(modelPresets, p => p.ModelPresetId, (p, id) => p.ModelPresetId = id);

            var activePersonaId = ResolveActivePresetId(personas, PromptSettingsControl.GetActivePersonaId(), p => p.PersonaId);
            var activeMemorySetId = ResolveActivePresetId(memorySets, EmbeddingSettingsControl.GetActiveMemorySetId(), p => p.MemorySetId);
            var activeModelPresetId = ResolveActivePresetId(modelPresets, LlmSettingsControl.GetActivePresetId(), p => p.ModelPresetId);

            if (string.IsNullOrWhiteSpace(activePersonaId)
                || string.IsNullOrWhiteSpace(activeMemorySetId)
                || string.IsNullOrWhiteSpace(activeModelPresetId))
            {
                throw new InvalidOperationException("アクティブな OtomeKairo 設定資源を解決できませんでした。");
            }

            return new OtomeKairoEditorState
            {
                Current = new OtomeKairoCurrentSettings
                {
                    SelectedPersonaId = activePersonaId,
                    SelectedMemorySetId = activeMemorySetId,
                    SelectedModelPresetId = activeModelPresetId,
                    WakePolicy = SystemSettingsControl.GetWakePolicy(),
                },
                Personas = ClonePersonas(personas),
                MemorySets = CloneMemorySets(memorySets),
                ModelPresets = CloneModelPresets(modelPresets),
            };
        }

        private static List<OtomeKairoPersonaDefinition> ClonePersonas(IEnumerable<OtomeKairoPersonaDefinition> personas)
        {
            return personas.Select(ClonePersona).ToList();
        }

        private static List<OtomeKairoMemorySetDefinition> CloneMemorySets(IEnumerable<OtomeKairoMemorySetDefinition> memorySets)
        {
            return memorySets.Select(CloneMemorySet).ToList();
        }

        private static List<OtomeKairoModelPresetDefinition> CloneModelPresets(IEnumerable<OtomeKairoModelPresetDefinition> modelPresets)
        {
            return modelPresets.Select(CloneModelPreset).ToList();
        }

        private static OtomeKairoPersonaDefinition ClonePersona(OtomeKairoPersonaDefinition persona)
        {
            return DeepClone(persona);
        }

        private static OtomeKairoMemorySetDefinition CloneMemorySet(OtomeKairoMemorySetDefinition memorySet)
        {
            return DeepClone(memorySet);
        }

        private static bool MemorySetDefinitionChanged(
            OtomeKairoMemorySetDefinition current,
            OtomeKairoMemorySetDefinition updated)
        {
            return !string.Equals(current.DisplayName, updated.DisplayName, StringComparison.Ordinal)
                || !EmbeddingDefinitionChanged(current.Embedding, updated.Embedding);
        }

        private static bool EmbeddingDefinitionChanged(
            Dictionary<string, object?>? current,
            Dictionary<string, object?>? updated)
        {
            var currentJson = JsonSerializer.Serialize(current ?? new Dictionary<string, object?>());
            var updatedJson = JsonSerializer.Serialize(updated ?? new Dictionary<string, object?>());
            return !string.Equals(currentJson, updatedJson, StringComparison.Ordinal);
        }

        private static OtomeKairoModelPresetDefinition CloneModelPreset(OtomeKairoModelPresetDefinition modelPreset)
        {
            return DeepClone(modelPreset);
        }

        private static T DeepClone<T>(T value)
        {
            var json = JsonSerializer.Serialize(value);
            var clone = JsonSerializer.Deserialize<T>(json);
            if (clone == null)
            {
                throw new InvalidOperationException($"型 {typeof(T).Name} の複製に失敗しました。");
            }
            return clone;
        }

        /// <summary>
        /// 元の設定に戻す（一設定などがあるためDisplayのみ復元が必要）
        /// </summary>
        private void RestoreOriginalSettings()
        {
            // Display の復元
            DisplaySettingsControl.ApplySnapshotToAppSettings(_originalDisplaySettings);
            DisplaySettingsControl.InitializeFromAppSettings();

            // アバターリストの復元
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
        /// アバター設定のディープコピーを作成
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
                sttProfileId = source.sttProfileId,
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

            // スクショ除外（ウィンドウタイトル正規表現 / ローカル設定）
            appSettings.ScreenshotSettings.excludePatterns = (List<string>)snapshot["WindowTitleExcludePatterns"];

            // 視覚キャプチャ（アイドルタイムアウト / ローカル設定）
            appSettings.ScreenshotSettings.idleTimeoutMinutes = (int)snapshot["VisualCaptureIdleTimeoutMinutes"];
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
                    // 現在のアバターの設定を更新
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
        /// OtomeKairoを再起動する
        /// </summary>
        private async Task RestartOtomeKairoAsync()
        {
            try
            {
                // --- リモート接続時はローカルプロセス再起動を行わない ---
                if (!AppSettings.Instance.IsOtomeKairoLocal())
                {
                    Debug.WriteLine("OtomeKairo はリモート接続設定のため、ローカル再起動をスキップします。");
                    return;
                }

                // MainWindowのインスタンスを取得
                var mainWindow = Application.Current.MainWindow as MainWindow;
                if (mainWindow != null)
                {
                    // 再起動開始を通知して起動待ち状態に戻す
                    _communicationService?.NotifyOtomeKairoRestarting();

                    // ProcessOperation.RestartIfRunning を指定してOtomeKairoを再起動（非同期）
                    await mainWindow.LaunchOtomeKairoAsync(ProcessOperation.RestartIfRunning);
                    Debug.WriteLine("OtomeKairoを再起動要求をしました");

                    // 再起動完了を待機
                    await WaitForOtomeKairoRestartAsync();
                    Debug.WriteLine("OtomeKairoの再起動が完了しました");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"OtomeKairo再起動中にエラーが発生しました: {ex.Message}");
                throw new Exception($"OtomeKairoの再起動に失敗しました: {ex.Message}");
            }
        }

        /// <summary>
        /// OtomeKairoの再起動完了を待機
        /// </summary>
        private async Task WaitForOtomeKairoRestartAsync()
        {
            // 通信サービスがない場合は待機できない
            if (_communicationService == null)
            {
                return;
            }

            await OtomeKairoStatusAwaiter
                .WaitUntilReadyAsync(_communicationService, TimeSpan.FromSeconds(120));
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
        /// OtomeKairo再起動が必要な設定項目が変更されたかどうかをチェック
        /// </summary>
        /// <param name="previousSettings">以前の設定</param>
        /// <param name="currentSettings">現在の設定</param>
        /// <returns>OtomeKairo再起動が必要な変更があった場合true</returns>
        private bool HasOtomeKairoRestartRequiredChanges(ConfigSettings previousSettings, ConfigSettings currentSettings)
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

            // アバターリストの比較
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
                CocoroShellProcessManager.Apply(AppSettings.Instance, ProcessOperation.RestartIfRunning);
                _communicationService?.ResetShellConnectionState();
                Debug.WriteLine("CocoroShellを再起動しました");
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
