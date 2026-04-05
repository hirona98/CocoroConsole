using CocoroConsole.Communication;
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
            EmbeddingSettingsControl.LlmApiKeyProvider = () => LlmSettingsControl.GetCurrentLlmApiKey();

            // otomekairo APIクライアントを初期化
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

            // API説明コントロールを初期化
            _ = ApiDocumentationControl.InitializeAsync();

            // プリセット管理コントロールを初期化
            _ = InitializePresetControlsAsync();

            // 元の設定のバックアップを作成
            BackupSettings();

            // OtomeKairo再起動チェック用に現在の設定のディープコピーを保存
            _previousCocoroCoreSettings = AppSettings.Instance.GetConfigSettings().DeepCopy();
        }

        private async Task InitializeSystemSettingsAsync()
        {
            await SystemSettingsControl.InitializeAsync();
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
                // --- OtomeKairo の editor-state を取得して UI へ変換する ---
                _loadedOtomeKairoEditorState = await _apiClient.GetEditorStateAsync();

                // --- LLM設定をリスト全体でロードする ---
                List<LlmPreset> llmPresets = BuildLlmPresetsFromEditorState(_loadedOtomeKairoEditorState);
                LlmSettingsControl.SetApiClient(_apiClient, SaveLlmPresetsToApiAsync);
                LlmSettingsControl.LoadSettingsList(llmPresets, _loadedOtomeKairoEditorState.Current.SelectedModelPresetId);

                // --- Embedding設定をリスト全体でロードする ---
                List<EmbeddingPreset> embeddingPresets = BuildEmbeddingPresetsFromEditorState(_loadedOtomeKairoEditorState);
                EmbeddingSettingsControl.SetApiClient(_apiClient, SaveEmbeddingPresetsToApiAsync);
                EmbeddingSettingsControl.IsMemoryEnabled = _loadedOtomeKairoEditorState.Current.MemoryEnabled;
                EmbeddingSettingsControl.LoadSettingsList(embeddingPresets, _loadedOtomeKairoEditorState.Current.SelectedModelPresetId);

                // --- Promptプリセットをロードする ---
                PromptSettingsControl.SetApiClient(_apiClient, SaveAllSettingsToApiAsync);
                PromptSettingsControl.LoadSettings(
                    BuildPersonaPresetsFromEditorState(_loadedOtomeKairoEditorState),
                    _loadedOtomeKairoEditorState.Current.SelectedPersonaId,
                    BuildAddonPresetsFromEditorState(_loadedOtomeKairoEditorState),
                    _loadedOtomeKairoEditorState.Current.SelectedPersonaId
                );

                // --- desktop_watch などの現在設定を System タブへ反映する ---
                SystemSettingsControl.ApplyOtomeKairoCurrentSettings(_loadedOtomeKairoEditorState.Current);

                // --- 設定変更イベントを登録する ---
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

            // スクショ除外（ウィンドウタイトル正規表現 / ローカル設定）
            dict["WindowTitleExcludePatterns"] = SystemSettingsControl.GetWindowTitleExcludePatterns();

            // デスクトップウォッチ（アイドルタイムアウト / ローカル設定）
            dict["DesktopWatchIdleTimeoutMinutes"] = SystemSettingsControl.GetDesktopWatchIdleTimeoutMinutes();

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
                HasOtomeKairoRestartRequiredChanges(_previousCocoroCoreSettings, currentSettings);

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
                    await _communicationService.RefreshOtomeKairoSettingsAsync();
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
            // --- 保存時点の接続設定（host/port/token）で API クライアントを再構築する ---
            InitializeApiClient();
            SystemSettingsControl.SetApiClient(_apiClient);
            if (_apiClient == null) return;
            LlmSettingsControl.SetApiClient(_apiClient, SaveLlmPresetsToApiAsync);
            EmbeddingSettingsControl.SetApiClient(_apiClient, SaveEmbeddingPresetsToApiAsync);
            PromptSettingsControl.SetApiClient(_apiClient, SaveAllSettingsToApiAsync);

            try
            {
                // --- 未読込なら最新 editor-state を先に取得する ---
                _loadedOtomeKairoEditorState ??= await _apiClient.GetEditorStateAsync();

                // --- UI から OtomeKairo の editor-state を組み立てる ---
                var request = BuildEditorStateFromUi(_loadedOtomeKairoEditorState);
                var updated = await _apiClient.ReplaceEditorStateAsync(request);
                _loadedOtomeKairoEditorState = updated;
                Debug.WriteLine("[SettingWindow] editor-state を API に保存しました");

                // --- reminders は未実装のため no-op だが、保存フローは統一して呼ぶ ---
                await SystemSettingsControl.SaveRemindersToApiAsync();

                // --- 保存後の server 状態で UI を再同期する ---
                LlmSettingsControl.LoadSettingsList(
                    BuildLlmPresetsFromEditorState(updated),
                    updated.Current.SelectedModelPresetId
                );

                EmbeddingSettingsControl.IsMemoryEnabled = updated.Current.MemoryEnabled;
                EmbeddingSettingsControl.LoadSettingsList(
                    BuildEmbeddingPresetsFromEditorState(updated),
                    updated.Current.SelectedModelPresetId
                );

                PromptSettingsControl.LoadSettings(
                    BuildPersonaPresetsFromEditorState(updated),
                    updated.Current.SelectedPersonaId,
                    BuildAddonPresetsFromEditorState(updated),
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

        private List<LlmPreset> BuildLlmPresetsFromEditorState(OtomeKairoEditorState editorState)
        {
            // --- model_preset と generation profile から LLM UI 用 DTO を組み立てる ---
            var profilesById = editorState.ModelProfiles.ToDictionary(p => p.ModelProfileId, StringComparer.OrdinalIgnoreCase);
            return editorState.ModelPresets
                .Select(preset => BuildLlmPreset(preset, profilesById))
                .ToList();
        }

        private List<EmbeddingPreset> BuildEmbeddingPresetsFromEditorState(OtomeKairoEditorState editorState)
        {
            // --- model_preset と embedding profile から Embedding UI 用 DTO を組み立てる ---
            var profilesById = editorState.ModelProfiles.ToDictionary(p => p.ModelProfileId, StringComparer.OrdinalIgnoreCase);
            return editorState.ModelPresets
                .Select(preset => BuildEmbeddingPreset(preset, profilesById))
                .ToList();
        }

        private List<PersonaPreset> BuildPersonaPresetsFromEditorState(OtomeKairoEditorState editorState)
        {
            // --- persona 資源を persona preset UI に写す ---
            return editorState.Personas
                .Select(persona => new PersonaPreset
                {
                    PersonaPresetId = persona.PersonaId,
                    PersonaPresetName = persona.DisplayName,
                    PersonaText = persona.PersonaText ?? string.Empty,
                    SecondPersonLabel = persona.SecondPersonLabel ?? string.Empty,
                })
                .ToList();
        }

        private List<AddonPreset> BuildAddonPresetsFromEditorState(OtomeKairoEditorState editorState)
        {
            // --- persona.addon_text を addon preset UI に写す ---
            return editorState.Personas
                .Select(persona => new AddonPreset
                {
                    AddonPresetId = persona.PersonaId,
                    AddonPresetName = persona.DisplayName,
                    AddonText = persona.AddonText ?? string.Empty,
                })
                .ToList();
        }

        private OtomeKairoEditorState BuildEditorStateFromUi(OtomeKairoEditorState baseState)
        {
            // --- UI 上の preset 群から editor-state 全体を再構成する ---
            List<LlmPreset> llmPresets = LlmSettingsControl.GetAllPresets();
            List<EmbeddingPreset> embeddingPresets = EmbeddingSettingsControl.GetAllPresets();
            List<PersonaPreset> personaPresets = PromptSettingsControl.GetAllPersonaPresets();
            List<AddonPreset> addonPresets = PromptSettingsControl.GetAllAddonPresets();

            EnsurePresetIds(llmPresets, p => p.LlmPresetId, (p, id) => p.LlmPresetId = id);
            EnsurePresetIds(embeddingPresets, p => p.EmbeddingPresetId, (p, id) => p.EmbeddingPresetId = id);
            EnsurePresetIds(personaPresets, p => p.PersonaPresetId, (p, id) => p.PersonaPresetId = id);
            EnsurePresetIds(addonPresets, p => p.AddonPresetId, (p, id) => p.AddonPresetId = id);

            var activeModelPresetId = ResolveActivePresetId(llmPresets, LlmSettingsControl.GetActivePresetId(), p => p.LlmPresetId)
                ?? ResolveActivePresetId(embeddingPresets, EmbeddingSettingsControl.GetActivePresetId(), p => p.EmbeddingPresetId);
            var activePersonaId = ResolveActivePresetId(personaPresets, PromptSettingsControl.GetActivePersonaPresetId(), p => p.PersonaPresetId)
                ?? ResolveActivePresetId(addonPresets, PromptSettingsControl.GetActiveAddonPresetId(), p => p.AddonPresetId);

            if (string.IsNullOrWhiteSpace(activeModelPresetId) || string.IsNullOrWhiteSpace(activePersonaId))
            {
                throw new InvalidOperationException("アクティブな OtomeKairo 設定資源を解決できませんでした。");
            }

            var personas = BuildPersonasFromUi(baseState, personaPresets, addonPresets);
            var modelResources = BuildModelResourcesFromUi(baseState, llmPresets, embeddingPresets);
            bool desktopWatchEnabled = SystemSettingsControl.GetDesktopWatchEnabled();
            string? preservedTargetClientId = baseState.Current.DesktopWatch?.TargetClientId;

            // --- memory_set は現 UI で編集しないため、読み込んだ定義をそのまま維持する ---
            return new OtomeKairoEditorState
            {
                Current = new OtomeKairoCurrentSettings
                {
                    SelectedPersonaId = activePersonaId,
                    SelectedMemorySetId = FirstNonEmpty(
                        baseState.Current.SelectedMemorySetId,
                        baseState.MemorySets.FirstOrDefault()?.MemorySetId,
                        "memory_set:default"
                    ),
                    SelectedModelPresetId = activeModelPresetId,
                    MemoryEnabled = EmbeddingSettingsControl.IsMemoryEnabled,
                    DesktopWatch = new OtomeKairoDesktopWatchSettings
                    {
                        Enabled = desktopWatchEnabled,
                        IntervalSeconds = SystemSettingsControl.GetDesktopWatchIntervalSeconds(),
                        TargetClientId = desktopWatchEnabled ? AppSettings.Instance.ClientId : preservedTargetClientId,
                    },
                    WakePolicy = CloneObjectMap(baseState.Current.WakePolicy),
                },
                Personas = personas,
                MemorySets = baseState.MemorySets
                    .Select(memorySet => new OtomeKairoMemorySetDefinition
                    {
                        MemorySetId = memorySet.MemorySetId,
                        DisplayName = memorySet.DisplayName,
                        Description = memorySet.Description,
                    })
                    .ToList(),
                ModelPresets = modelResources.ModelPresets,
                ModelProfiles = modelResources.ModelProfiles,
            };
        }

        private List<OtomeKairoPersonaDefinition> BuildPersonasFromUi(
            OtomeKairoEditorState baseState,
            IReadOnlyList<PersonaPreset> personaPresets,
            IReadOnlyList<AddonPreset> addonPresets)
        {
            // --- persona preset と addon preset を 1 つの persona 資源に統合する ---
            var existingById = baseState.Personas.ToDictionary(p => p.PersonaId, StringComparer.OrdinalIgnoreCase);
            var personaById = personaPresets
                .Where(p => !string.IsNullOrWhiteSpace(p.PersonaPresetId))
                .ToDictionary(p => p.PersonaPresetId!, StringComparer.OrdinalIgnoreCase);
            var addonById = addonPresets
                .Where(p => !string.IsNullOrWhiteSpace(p.AddonPresetId))
                .ToDictionary(p => p.AddonPresetId!, StringComparer.OrdinalIgnoreCase);
            var orderedIds = personaPresets.Select(p => p.PersonaPresetId!)
                .Concat(addonPresets.Select(p => p.AddonPresetId!))
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // --- addon 側だけで追加された preset も、最小 persona 定義を補って保持する ---
            return orderedIds
                .Select(id =>
                {
                    existingById.TryGetValue(id, out var existing);
                    personaById.TryGetValue(id, out var personaPreset);
                    addonById.TryGetValue(id, out var addonPreset);
                    return new OtomeKairoPersonaDefinition
                    {
                        PersonaId = id,
                        DisplayName = FirstNonEmpty(
                            personaPreset?.PersonaPresetName,
                            addonPreset?.AddonPresetName,
                            existing?.DisplayName,
                            id
                        ),
                        PersonaText = FirstNonEmpty(
                            personaPreset?.PersonaText,
                            existing?.PersonaText,
                            personaPreset?.PersonaPresetName,
                            addonPreset?.AddonPresetName,
                            id
                        ),
                        SecondPersonLabel = FirstNonEmpty(
                            personaPreset?.SecondPersonLabel,
                            existing?.SecondPersonLabel,
                            "あなた"
                        ),
                        AddonText = FirstNonEmpty(
                            addonPreset?.AddonText,
                            existing?.AddonText,
                            string.Empty
                        ),
                        CorePersona = BuildCorePersona(existing),
                        ExpressionStyle = BuildExpressionStyle(existing),
                    };
                })
                .ToList();
        }

        private (List<OtomeKairoModelPresetDefinition> ModelPresets, List<OtomeKairoModelProfileDefinition> ModelProfiles) BuildModelResourcesFromUi(
            OtomeKairoEditorState baseState,
            IReadOnlyList<LlmPreset> llmPresets,
            IReadOnlyList<EmbeddingPreset> embeddingPresets)
        {
            // --- LLM preset と Embedding preset を 1 つの model_preset 群へ統合する ---
            var existingPresetsById = baseState.ModelPresets.ToDictionary(p => p.ModelPresetId, StringComparer.OrdinalIgnoreCase);
            var existingProfilesById = baseState.ModelProfiles.ToDictionary(p => p.ModelProfileId, StringComparer.OrdinalIgnoreCase);
            var llmById = llmPresets
                .Where(p => !string.IsNullOrWhiteSpace(p.LlmPresetId))
                .ToDictionary(p => p.LlmPresetId!, StringComparer.OrdinalIgnoreCase);
            var embeddingById = embeddingPresets
                .Where(p => !string.IsNullOrWhiteSpace(p.EmbeddingPresetId))
                .ToDictionary(p => p.EmbeddingPresetId!, StringComparer.OrdinalIgnoreCase);
            var orderedIds = llmPresets.Select(p => p.LlmPresetId!)
                .Concat(embeddingPresets.Select(p => p.EmbeddingPresetId!))
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var modelPresets = new List<OtomeKairoModelPresetDefinition>();
            var modelProfiles = new List<OtomeKairoModelProfileDefinition>();

            foreach (var presetId in orderedIds)
            {
                existingPresetsById.TryGetValue(presetId, out var existingPreset);
                llmById.TryGetValue(presetId, out var llmPreset);
                embeddingById.TryGetValue(presetId, out var embeddingPreset);

                var generationProfileId = ResolveGenerationProfileId(existingPreset, presetId);
                var embeddingProfileId = ResolveEmbeddingProfileId(existingPreset, presetId);
                existingProfilesById.TryGetValue(generationProfileId, out var existingGenerationProfile);
                existingProfilesById.TryGetValue(embeddingProfileId, out var existingEmbeddingProfile);

                var generationProfile = BuildGenerationProfile(
                    presetId,
                    llmPreset,
                    existingGenerationProfile,
                    generationProfileId
                );
                var embeddingProfile = BuildEmbeddingProfile(
                    presetId,
                    embeddingPreset,
                    existingEmbeddingProfile,
                    embeddingProfileId
                );

                modelProfiles.Add(generationProfile);
                modelProfiles.Add(embeddingProfile);
                modelPresets.Add(BuildModelPresetDefinition(
                    presetId,
                    llmPreset,
                    embeddingPreset,
                    existingPreset,
                    generationProfileId,
                    embeddingProfileId
                ));
            }

            return (modelPresets, modelProfiles);
        }

        private static LlmPreset BuildLlmPreset(
            OtomeKairoModelPresetDefinition modelPreset,
            IReadOnlyDictionary<string, OtomeKairoModelProfileDefinition> profilesById)
        {
            // --- reply_generation と generation profile を UI 用の 1 件へ投影する ---
            var replyRole = GetRole(modelPreset, "reply_generation");
            var profileId = ReadString(replyRole, "model_profile_id");
            profilesById.TryGetValue(profileId ?? string.Empty, out var generationProfile);

            return new LlmPreset
            {
                LlmPresetId = modelPreset.ModelPresetId,
                LlmPresetName = modelPreset.DisplayName,
                LlmApiKey = ReadAuthToken(generationProfile?.Auth) ?? string.Empty,
                LlmModel = generationProfile?.Model ?? string.Empty,
                LlmBaseUrl = generationProfile?.BaseUrl,
                MaxTurnsWindow = ReadInt(replyRole, "max_turns_window", LlmPreset.DefaultMaxTurnsWindow),
                MaxTokens = ReadInt(replyRole, "max_tokens", LlmPreset.DefaultMaxTokens),
                ReasoningEffort = ReadString(replyRole, "reasoning_effort"),
                ReplyWebSearchEnabled = ReadBool(replyRole, "reply_web_search_enabled", true),
                ImageModelApiKey = ReadAuthToken(generationProfile?.VisionAuth),
                ImageModel = generationProfile?.VisionModelName ?? string.Empty,
                ImageLlmBaseUrl = generationProfile?.VisionBaseUrl,
                MaxTokensVision = generationProfile?.VisionMaxTokens ?? LlmPreset.DefaultMaxTokensVision,
                ImageTimeoutSeconds = generationProfile?.VisionTimeoutSeconds ?? LlmPreset.DefaultImageTimeoutSeconds,
            };
        }

        private static EmbeddingPreset BuildEmbeddingPreset(
            OtomeKairoModelPresetDefinition modelPreset,
            IReadOnlyDictionary<string, OtomeKairoModelProfileDefinition> profilesById)
        {
            // --- embedding role と embedding profile を UI 用の 1 件へ投影する ---
            var embeddingRole = GetRole(modelPreset, "embedding");
            var profileId = ReadString(embeddingRole, "model_profile_id");
            profilesById.TryGetValue(profileId ?? string.Empty, out var embeddingProfile);

            return new EmbeddingPreset
            {
                EmbeddingPresetId = modelPreset.ModelPresetId,
                EmbeddingPresetName = modelPreset.DisplayName,
                EmbeddingModelApiKey = ReadAuthToken(embeddingProfile?.Auth),
                EmbeddingModel = embeddingProfile?.Model ?? string.Empty,
                EmbeddingBaseUrl = embeddingProfile?.BaseUrl,
                EmbeddingDimension = ReadInt(embeddingRole, "embedding_dimension", EmbeddingPreset.DefaultEmbeddingDimension),
                SimilarEpisodesLimit = ReadInt(embeddingRole, "similar_episodes_limit", EmbeddingPreset.DefaultSimilarEpisodesLimit),
            };
        }

        private static OtomeKairoModelPresetDefinition BuildModelPresetDefinition(
            string presetId,
            LlmPreset? llmPreset,
            EmbeddingPreset? embeddingPreset,
            OtomeKairoModelPresetDefinition? existingPreset,
            string generationProfileId,
            string embeddingProfileId)
        {
            // --- UI から編集できる値だけ role 辞書へ反映し、その他は既存値を引き継ぐ ---
            var replyRole = CloneRole(GetRole(existingPreset, "reply_generation"));
            replyRole["model_profile_id"] = generationProfileId;
            replyRole["max_turns_window"] = llmPreset?.MaxTurnsWindow ?? ReadInt(replyRole, "max_turns_window", LlmPreset.DefaultMaxTurnsWindow);
            replyRole["max_tokens"] = llmPreset?.MaxTokens ?? ReadInt(replyRole, "max_tokens", LlmPreset.DefaultMaxTokens);
            replyRole["reply_web_search_enabled"] = llmPreset?.ReplyWebSearchEnabled ?? ReadBool(replyRole, "reply_web_search_enabled", true);
            if (!string.IsNullOrWhiteSpace(llmPreset?.ReasoningEffort))
            {
                replyRole["reasoning_effort"] = llmPreset!.ReasoningEffort!;
            }
            else
            {
                replyRole.Remove("reasoning_effort");
            }

            var decisionRole = CloneRole(GetRole(existingPreset, "decision_generation"));
            decisionRole["model_profile_id"] = generationProfileId;
            if (!decisionRole.ContainsKey("max_tokens"))
            {
                decisionRole["max_tokens"] = llmPreset?.MaxTokens ?? LlmPreset.DefaultMaxTokens;
            }

            var recallRole = CloneRole(GetRole(existingPreset, "recall_hint_generation"));
            recallRole["model_profile_id"] = generationProfileId;
            if (!recallRole.ContainsKey("max_tokens"))
            {
                recallRole["max_tokens"] = 2048;
            }

            var memoryRole = CloneRole(GetRole(existingPreset, "memory_interpretation"));
            memoryRole["model_profile_id"] = generationProfileId;
            if (!memoryRole.ContainsKey("max_tokens"))
            {
                memoryRole["max_tokens"] = llmPreset?.MaxTokens ?? LlmPreset.DefaultMaxTokens;
            }

            var embeddingRole = CloneRole(GetRole(existingPreset, "embedding"));
            embeddingRole["model_profile_id"] = embeddingProfileId;
            embeddingRole["embedding_dimension"] = embeddingPreset?.EmbeddingDimension ?? ReadInt(embeddingRole, "embedding_dimension", EmbeddingPreset.DefaultEmbeddingDimension);
            embeddingRole["similar_episodes_limit"] = embeddingPreset?.SimilarEpisodesLimit ?? ReadInt(embeddingRole, "similar_episodes_limit", EmbeddingPreset.DefaultSimilarEpisodesLimit);

            return new OtomeKairoModelPresetDefinition
            {
                ModelPresetId = presetId,
                DisplayName = FirstNonEmpty(
                    llmPreset?.LlmPresetName,
                    embeddingPreset?.EmbeddingPresetName,
                    existingPreset?.DisplayName,
                    presetId
                ),
                Roles = new Dictionary<string, Dictionary<string, object?>>
                {
                    ["reply_generation"] = replyRole,
                    ["decision_generation"] = decisionRole,
                    ["recall_hint_generation"] = recallRole,
                    ["memory_interpretation"] = memoryRole,
                    ["embedding"] = embeddingRole,
                },
            };
        }

        private static OtomeKairoModelProfileDefinition BuildGenerationProfile(
            string presetId,
            LlmPreset? llmPreset,
            OtomeKairoModelProfileDefinition? existingProfile,
            string profileId)
        {
            // --- 生成系 profile は model 文字列を正本にして組み立てる ---
            string model = llmPreset?.LlmModel?.Trim() ?? string.Empty;
            string? baseUrl = NormalizeEmptyToNull(llmPreset?.LlmBaseUrl);
            string apiKey = llmPreset?.LlmApiKey?.Trim() ?? string.Empty;

            return new OtomeKairoModelProfileDefinition
            {
                ModelProfileId = profileId,
                DisplayName = FirstNonEmpty(llmPreset?.LlmPresetName, existingProfile?.DisplayName, $"{presetId} Text"),
                Kind = "generation",
                Model = model,
                BaseUrl = baseUrl,
                Auth = BuildAuth(apiKey),
                VisionModelName = NormalizeEmptyToNull(llmPreset?.ImageModel),
                VisionBaseUrl = NormalizeEmptyToNull(llmPreset?.ImageLlmBaseUrl),
                VisionAuth = string.IsNullOrWhiteSpace(llmPreset?.ImageModelApiKey) && string.IsNullOrWhiteSpace(llmPreset?.ImageLlmBaseUrl) && string.IsNullOrWhiteSpace(llmPreset?.ImageModel)
                    ? null
                    : BuildAuth(llmPreset?.ImageModelApiKey),
                VisionMaxTokens = llmPreset?.MaxTokensVision,
                VisionTimeoutSeconds = llmPreset?.ImageTimeoutSeconds,
            };
        }

        private static OtomeKairoModelProfileDefinition BuildEmbeddingProfile(
            string presetId,
            EmbeddingPreset? embeddingPreset,
            OtomeKairoModelProfileDefinition? existingProfile,
            string profileId)
        {
            // --- embedding profile も model 文字列を正本にして組み立てる ---
            string model = embeddingPreset?.EmbeddingModel?.Trim() ?? string.Empty;
            string? baseUrl = NormalizeEmptyToNull(embeddingPreset?.EmbeddingBaseUrl);
            string apiKey = embeddingPreset?.EmbeddingModelApiKey?.Trim() ?? string.Empty;

            return new OtomeKairoModelProfileDefinition
            {
                ModelProfileId = profileId,
                DisplayName = FirstNonEmpty(embeddingPreset?.EmbeddingPresetName, existingProfile?.DisplayName, $"{presetId} Embedding"),
                Kind = "embedding",
                Model = model,
                BaseUrl = baseUrl,
                Auth = BuildAuth(apiKey),
            };
        }

        private static Dictionary<string, object?> BuildCorePersona(OtomeKairoPersonaDefinition? existing)
        {
            // --- persona の中核は既存値を優先し、無ければ最小既定値を補う ---
            var result = CloneObjectMap(existing?.CorePersona);
            if (!result.ContainsKey("self_image"))
            {
                result["self_image"] = "long-term companion";
            }
            if (!result.ContainsKey("judgement_style"))
            {
                result["judgement_style"] = "careful and warm";
            }
            if (!result.ContainsKey("relation_baseline"))
            {
                result["relation_baseline"] = "supportive";
            }
            return result;
        }

        private static Dictionary<string, object?> BuildExpressionStyle(OtomeKairoPersonaDefinition? existing)
        {
            // --- expression_style.tone は必須なので、必ず埋める ---
            var result = CloneObjectMap(existing?.ExpressionStyle);
            if (!result.ContainsKey("tone"))
            {
                result["tone"] = "gentle";
            }
            if (!result.ContainsKey("sentence_length"))
            {
                result["sentence_length"] = "medium";
            }
            if (!result.ContainsKey("emotional_expressiveness"))
            {
                result["emotional_expressiveness"] = "moderate";
            }
            return result;
        }

        private static Dictionary<string, object?> CloneObjectMap(Dictionary<string, object?>? source)
        {
            // --- shallow copy で十分な設定辞書を複製する ---
            return source == null
                ? new Dictionary<string, object?>()
                : source.ToDictionary(entry => entry.Key, entry => entry.Value);
        }

        private static Dictionary<string, object?> CloneRole(Dictionary<string, object?>? source)
        {
            // --- role 辞書も shallow copy で扱う ---
            return source == null
                ? new Dictionary<string, object?>()
                : source.ToDictionary(entry => entry.Key, entry => entry.Value);
        }

        private static Dictionary<string, object?> GetRole(OtomeKairoModelPresetDefinition? preset, string roleName)
        {
            // --- role が無い場合でも空辞書を返し、呼び出し側で埋める ---
            if (preset == null || preset.Roles == null || !preset.Roles.TryGetValue(roleName, out var role) || role == null)
            {
                return new Dictionary<string, object?>();
            }

            return role;
        }

        private static string ResolveGenerationProfileId(OtomeKairoModelPresetDefinition? existingPreset, string presetId)
        {
            // --- 既存 preset があれば reply_generation の profile id を維持する ---
            var existingId = ReadString(GetRole(existingPreset, "reply_generation"), "model_profile_id");
            return !string.IsNullOrWhiteSpace(existingId) ? existingId! : $"model_profile:{presetId}:generation";
        }

        private static string ResolveEmbeddingProfileId(OtomeKairoModelPresetDefinition? existingPreset, string presetId)
        {
            // --- 既存 preset があれば embedding の profile id を維持する ---
            var existingId = ReadString(GetRole(existingPreset, "embedding"), "model_profile_id");
            return !string.IsNullOrWhiteSpace(existingId) ? existingId! : $"model_profile:{presetId}:embedding";
        }

        private static Dictionary<string, object?> BuildAuth(string? apiKey)
        {
            // --- API キー未設定もそのまま表現できるよう、none を許す ---
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return new Dictionary<string, object?>
                {
                    ["type"] = "none",
                };
            }

            return new Dictionary<string, object?>
            {
                ["type"] = "bearer",
                ["token"] = apiKey,
            };
        }

        private static string? ReadAuthToken(Dictionary<string, object?>? auth)
        {
            // --- OtomeKairo 側へ書いた bearer token を UI 用文字列へ戻す ---
            if (auth == null)
            {
                return null;
            }

            return ReadString(auth, "token");
        }

        private static string? ReadString(Dictionary<string, object?> values, string key)
        {
            // --- object/JsonElement の混在を吸収して string を読む ---
            if (!values.TryGetValue(key, out var value))
            {
                return null;
            }

            return ReadString(value);
        }

        private static string? ReadString(object? value)
        {
            if (value is null)
            {
                return null;
            }

            if (value is string text)
            {
                return text;
            }

            if (value is System.Text.Json.JsonElement element)
            {
                if (element.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    return element.GetString();
                }
                if (element.ValueKind != System.Text.Json.JsonValueKind.Null && element.ValueKind != System.Text.Json.JsonValueKind.Undefined)
                {
                    return element.ToString();
                }
            }

            return value.ToString();
        }

        private static int ReadInt(Dictionary<string, object?> values, string key, int fallback)
        {
            // --- int 系の role 値を安全に読む ---
            if (!values.TryGetValue(key, out var value))
            {
                return fallback;
            }

            if (value is int intValue)
            {
                return intValue;
            }

            if (value is long longValue)
            {
                return (int)longValue;
            }

            if (value is System.Text.Json.JsonElement element)
            {
                if (element.ValueKind == System.Text.Json.JsonValueKind.Number && element.TryGetInt32(out var jsonValue))
                {
                    return jsonValue;
                }
                if (element.ValueKind == System.Text.Json.JsonValueKind.String && int.TryParse(element.GetString(), out var parsed))
                {
                    return parsed;
                }
            }

            if (int.TryParse(value?.ToString(), out var direct))
            {
                return direct;
            }

            return fallback;
        }

        private static bool ReadBool(Dictionary<string, object?> values, string key, bool fallback)
        {
            // --- bool 系の role 値を安全に読む ---
            if (!values.TryGetValue(key, out var value))
            {
                return fallback;
            }

            if (value is bool boolValue)
            {
                return boolValue;
            }

            if (value is System.Text.Json.JsonElement element)
            {
                if (element.ValueKind == System.Text.Json.JsonValueKind.True || element.ValueKind == System.Text.Json.JsonValueKind.False)
                {
                    return element.GetBoolean();
                }
                if (element.ValueKind == System.Text.Json.JsonValueKind.String && bool.TryParse(element.GetString(), out var parsed))
                {
                    return parsed;
                }
            }

            if (bool.TryParse(value?.ToString(), out var direct))
            {
                return direct;
            }

            return fallback;
        }

        private static string FirstNonEmpty(params string?[] values)
        {
            // --- 空文字を飛ばして最初の値を返す ---
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value!;
                }
            }

            return string.Empty;
        }

        private static string? NormalizeEmptyToNull(string? value)
        {
            // --- 空白だけの入力は null に寄せる ---
            var trimmed = value?.Trim();
            return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
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

            // デスクトップウォッチ（アイドルタイムアウト / ローカル設定）
            appSettings.ScreenshotSettings.idleTimeoutMinutes = (int)snapshot["DesktopWatchIdleTimeoutMinutes"];
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
            // 最大待機時間
            var timeout = TimeSpan.FromSeconds(120);

            // 通信サービスがない場合は待機できない
            if (_communicationService == null)
            {
                return;
            }

            // 既に起動完了状態なら即終了
            if (IsOtomeKairoReadyStatus(_communicationService.CurrentStatus))
            {
                return;
            }

            // ステータス変更を待つためのTCS
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            // ステータス変更イベント
            void OnStatusChanged(object? sender, OtomeKairoStatus status)
            {
                // 起動完了状態に戻ったら終了
                if (IsOtomeKairoReadyStatus(status))
                {
                    tcs.TrySetResult(true);
                }
            }

            // ステータス変更イベントを購読
            _communicationService.StatusChanged += OnStatusChanged;

            try
            {
                // 購読後に再確認（取りこぼし防止）
                if (IsOtomeKairoReadyStatus(_communicationService.CurrentStatus))
                {
                    return;
                }

                // タイムアウト付きで待機
                var completed = await Task.WhenAny(tcs.Task, Task.Delay(timeout));
                if (completed != tcs.Task)
                {
                    throw new TimeoutException("OtomeKairoの再起動がタイムアウトしました");
                }

                await tcs.Task;
            }
            finally
            {
                // ステータス変更イベントを解除
                _communicationService.StatusChanged -= OnStatusChanged;
            }
        }

        /// <summary>
        /// OtomeKairoが起動完了状態かどうかを判定する
        /// </summary>
        /// <param name="status">OtomeKairoのステータス</param>
        /// <returns>起動完了状態の場合true</returns>
        private static bool IsOtomeKairoReadyStatus(OtomeKairoStatus status)
        {
            // Normal / Processing は起動完了扱い
            return status == OtomeKairoStatus.Normal ||
                   status == OtomeKairoStatus.ProcessingMessage ||
                   status == OtomeKairoStatus.ProcessingImage;
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
