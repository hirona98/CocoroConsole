using CocoroConsole.Communication;
using CocoroConsole.Models;
using CocoroConsole.Models.CocoroGhostApi;
using CocoroConsole.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace CocoroConsole.Services
{
    /// <summary>
    /// アプリケーション設定を管理するクラス
    /// </summary>
    public class AppSettings : IAppSettings
    {
        private static readonly Lazy<AppSettings> _instance = new Lazy<AppSettings>(() => new AppSettings());

        public static AppSettings Instance => _instance.Value;

        /// <summary>
        /// 設定が保存されたときに発生するイベント
        /// </summary>
        public static event EventHandler? SettingsSaved;

        // UserDataディレクトリのパスを取得
        public string UserDataDirectory => FindUserDataDirectory();

        // アプリケーション設定ファイルのパス
        private string AppSettingsFilePath => Path.Combine(UserDataDirectory, "Setting.json");

        // デフォルト設定ファイルのパス
        private string DefaultSettingsFilePath => Path.Combine(UserDataDirectory, "DefaultSetting.json");

        // アニメーション設定ファイルのパス
        private string AnimationSettingsFilePath => Path.Combine(UserDataDirectory, "AnimationSettings.json");

        // デフォルトアニメーション設定ファイルのパス
        private string DefaultAnimationSettingsFilePath => Path.Combine(UserDataDirectory, "DefaultAnimationSettings.json");

        public int CocoroConsolePort { get; set; }
        public int CocoroGhostPort { get; set; }
        public int CocoroShellPort { get; set; }
        // /api/events/stream で hello を送るためのクライアントID（安定ID）
        public string ClientId { get; set; } = string.Empty;
        // cocoro_ghost API Bearer トークン
        public string CocoroGhostBearerToken { get; set; } = string.Empty;
        // LLMを使用するか
        public bool IsUseLLM { get; set; } = false;
        // UI設定
        public bool IsRestoreWindowPosition { get; set; }
        public bool IsTopmost { get; set; }
        public bool IsEscapeCursor { get; set; }
        public List<EscapePosition> EscapePositions { get; set; } = new List<EscapePosition>();
        public bool IsInputVirtualKey { get; set; }
        public string VirtualKeyString { get; set; } = string.Empty;
        public bool IsAutoMove { get; set; }
        public bool ShowMessageWindow { get; set; }
        public bool IsEnableAmbientOcclusion { get; set; }
        public int MsaaLevel { get; set; }
        public int CharacterShadow { get; set; }
        public int CharacterShadowResolution { get; set; }
        public int BackgroundShadow { get; set; }
        public int BackgroundShadowResolution { get; set; }
        public int WindowSize { get; set; }
        public float WindowPositionX { get; set; }
        public float WindowPositionY { get; set; }

        // キャラクター設定
        public int CurrentCharacterIndex { get; set; } = 0;
        public List<CharacterSettings> CharacterList { get; set; } = new List<CharacterSettings>();

        // アニメーション設定
        public int CurrentAnimationSettingIndex { get; set; } = 0;
        public List<AnimationSetting> AnimationSettings { get; set; } = new List<AnimationSetting>();

        // スクリーンショット設定
        public ScreenshotSettings ScreenshotSettings { get; set; } = new ScreenshotSettings();

        // マイク設定
        public MicrophoneSettings MicrophoneSettings { get; set; } = new MicrophoneSettings();

        // メッセージウィンドウ設定
        public MessageWindowSettings MessageWindowSettings { get; set; } = new MessageWindowSettings();

        public bool IsLoaded { get; set; } = false;

        // コンストラクタはprivate（シングルトンパターン）
        private AppSettings()
        {
            // 設定ファイルから読み込み
            LoadSettings();
        }

        /// <summary>
        /// UserDataディレクトリを探索して見つける
        /// </summary>
        /// <returns>UserDataディレクトリのパス</returns>
        private string FindUserDataDirectory()
        {
            var baseDirectory = AppContext.BaseDirectory;

            // 探索するパスの配列
            string[] searchPaths = {
#if !DEBUG
                Path.Combine(baseDirectory, "UserData"),
#endif
                Path.Combine(baseDirectory, "..", "UserData"),
                Path.Combine(baseDirectory, "..", "..", "UserData"),
                Path.Combine(baseDirectory, "..", "..", "..", "UserData"),
                Path.Combine(baseDirectory, "..", "..", "..", "..", "UserData")
            };

            foreach (var path in searchPaths)
            {
                var fullPath = Path.GetFullPath(path);
                if (Directory.Exists(fullPath))
                {
                    Debug.WriteLine($"UserDataディレクトリ: {fullPath}");
                    return fullPath;
                }
            }

            // 見つからない場合は、最初のパスを使用してディレクトリを作成
            var defaultPath = Path.GetFullPath(searchPaths[0]);
            Debug.WriteLine($"UserDataが見つからないため作成: {defaultPath}");
            Directory.CreateDirectory(defaultPath);
            return defaultPath;
        }

        /// <summary>
        /// 設定値を更新
        /// </summary>
        /// <param name="config">サーバーから受信した設定値</param>
        public void UpdateSettings(ConfigSettings config)
        {
            CocoroConsolePort = config.CocoroConsolePort;
            CocoroGhostPort = config.cocoroCorePort;
            CocoroShellPort = config.cocoroShellPort;
            ClientId = config.clientId;
            if (string.IsNullOrWhiteSpace(ClientId))
            {
                ClientId = $"console-{Guid.NewGuid()}";
            }
            CocoroGhostBearerToken = config.cocoroGhostBearerToken ?? string.Empty;
            IsUseLLM = config.isUseLLM;
            IsRestoreWindowPosition = config.isRestoreWindowPosition;
            IsTopmost = config.isTopmost;
            IsEscapeCursor = config.isEscapeCursor;
            EscapePositions = config.escapePositions != null ? new List<EscapePosition>(config.escapePositions) : new List<EscapePosition>();
            IsInputVirtualKey = config.isInputVirtualKey;
            VirtualKeyString = config.virtualKeyString ?? string.Empty;
            IsAutoMove = config.isAutoMove;
            ShowMessageWindow = config.showMessageWindow;
            IsEnableAmbientOcclusion = config.isEnableAmbientOcclusion;
            MsaaLevel = config.msaaLevel;
            CharacterShadow = config.characterShadow;
            CharacterShadowResolution = config.characterShadowResolution;
            BackgroundShadow = config.backgroundShadow;
            BackgroundShadowResolution = config.backgroundShadowResolution;
            WindowSize = config.windowSize > 0 ? (int)config.windowSize : WindowSize;
            WindowPositionX = config.windowPositionX;
            WindowPositionY = config.windowPositionY;
            CurrentCharacterIndex = config.currentCharacterIndex;

            // キャラクターリストを更新（もし受信したリストが空でなければ）
            if (config.characterList != null && config.characterList.Count > 0)
            {
                CharacterList = new List<CharacterSettings>(config.characterList);
            }

            EnsureCharacterSchemaConsistency();


            // スクリーンショット設定を更新
            if (config.screenshotSettings != null)
            {
                ScreenshotSettings = config.screenshotSettings;
            }

            // マイク設定を更新
            if (config.microphoneSettings != null)
            {
                MicrophoneSettings = config.microphoneSettings;
            }

            // メッセージウィンドウ設定を更新
            if (config.messageWindowSettings != null)
            {
                MessageWindowSettings = config.messageWindowSettings;
            }

            // 設定読み込み完了フラグを設定
            IsLoaded = true;
        }

        /// <summary>
        /// 新規追加項目などの不足を補完
        /// </summary>
        private void EnsureCharacterSchemaConsistency()
        {
            // キャラクター設定の不足補完が必要になった場合はここで行う
        }

        /// <summary>
        /// 現在の設定からConfigSettingsオブジェクトを作成
        /// </summary>
        /// <returns>ConfigSettings オブジェクト</returns>
        public ConfigSettings GetConfigSettings()
        {
            return new ConfigSettings
            {
                CocoroConsolePort = CocoroConsolePort,
                cocoroCorePort = CocoroGhostPort,
                cocoroShellPort = CocoroShellPort,
                clientId = ClientId,
                cocoroGhostBearerToken = CocoroGhostBearerToken,
                isUseLLM = IsUseLLM,
                isRestoreWindowPosition = IsRestoreWindowPosition,
                isTopmost = IsTopmost,
                isEscapeCursor = IsEscapeCursor,
                escapePositions = new List<EscapePosition>(EscapePositions),
                isInputVirtualKey = IsInputVirtualKey,
                virtualKeyString = VirtualKeyString,
                isAutoMove = IsAutoMove,
                showMessageWindow = ShowMessageWindow,
                isEnableAmbientOcclusion = IsEnableAmbientOcclusion,
                msaaLevel = MsaaLevel,
                characterShadow = CharacterShadow,
                characterShadowResolution = CharacterShadowResolution,
                backgroundShadow = BackgroundShadow,
                backgroundShadowResolution = BackgroundShadowResolution,
                windowSize = WindowSize,
                windowPositionX = WindowPositionX,
                windowPositionY = WindowPositionY,
                screenshotSettings = ScreenshotSettings,
                microphoneSettings = MicrophoneSettings,
                messageWindowSettings = MessageWindowSettings,
                currentCharacterIndex = CurrentCharacterIndex,
                characterList = new List<CharacterSettings>(CharacterList)
            };
        }


        /// <summary>
        /// 設定ファイルから設定を読み込む
        /// </summary>
        public void LoadSettings()
        {
            try
            {
                // アプリケーション設定ファイルを読み込む
                LoadAppSettings();
                // アニメーション設定ファイルを読み込む
                LoadAnimationSettings();
                // 設定読み込み完了フラグを設定
                IsLoaded = true;
            }
            catch (Exception ex)
            {
                // エラーが発生した場合はデフォルト設定を使用
                Debug.WriteLine($"設定ファイル読み込みエラー: {ex.Message}");
            }
        }

        /// <summary>
        /// アプリケーション設定ファイルを読み込む
        /// </summary>
        public void LoadAppSettings()
        {
            try
            {
                // ディレクトリの存在確認とない場合は作成
                EnsureUserDataDirectoryExists();

                // 設定ファイルが存在するか確認
                if (File.Exists(AppSettingsFilePath))
                {
                    LoadExistingSettingsFile();
                }
                else
                {
                    // 設定ファイルがない場合はデフォルト設定を適用して保存
                    var defaultSettings = LoadDefaultSettings();
                    UpdateSettings(defaultSettings);
                    SaveAppSettings();
                    Debug.WriteLine($"デフォルト設定をファイルに保存しました: {AppSettingsFilePath}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"アプリケーション設定ファイル読み込みエラー: {ex.Message}");
            }
        }

        /// <summary>
        /// UserDataディレクトリの存在を確認し、必要なら作成する
        /// </summary>
        private void EnsureUserDataDirectoryExists()
        {
            string userDataDir = UserDataDirectory;

            if (!Directory.Exists(userDataDir))
            {
                Directory.CreateDirectory(userDataDir);
            }
        }

        /// <summary>
        /// 既存の設定ファイルを読み込む
        /// </summary>
        private void LoadExistingSettingsFile()
        {
            string userSettingsJson = File.ReadAllText(AppSettingsFilePath);
            ProcessCurrentFormatSettings(userSettingsJson);
        }

        /// <summary>
        /// 現在のフォーマットの設定ファイルを処理する
        /// </summary>
        private void ProcessCurrentFormatSettings(string configJson)
        {
            var userSettings = MessageHelper.DeserializeFromJson<ConfigSettings>(configJson);
            if (userSettings != null)
            {
                var shouldPersistClientId = string.IsNullOrWhiteSpace(userSettings.clientId);
                UpdateSettings(userSettings);
                if (shouldPersistClientId && !string.IsNullOrWhiteSpace(ClientId))
                {
                    SaveAppSettings();
                }
            }
        }

        /// <summary>
        /// デフォルト設定ファイルを読み込む
        /// </summary>
        private ConfigSettings LoadDefaultSettings()
        {
            if (File.Exists(DefaultSettingsFilePath))
            {
                try
                {
                    string json = File.ReadAllText(DefaultSettingsFilePath);
                    var defaultSettings = MessageHelper.DeserializeFromJson<ConfigSettings>(json);

                    if (defaultSettings != null)
                    {
                        return defaultSettings;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"デフォルト設定ファイル読み込みエラー: {ex.Message}");
                }
            }

            // 読み込みに失敗した場合は空の設定を返す
            return new ConfigSettings();
        }

        /// <summary>
        /// DefaultSetting.json からキャラクターの雛形を作成する
        /// </summary>
        public CharacterSettings CreateCharacterFromDefaults(string modelName)
        {
            var defaults = LoadDefaultSettings();
            var template = defaults.characterList != null && defaults.characterList.Count > 0
                ? defaults.characterList[0]
                : new CharacterSettings();

            var character = template.DeepCopy();
            character.modelName = modelName;
            character.vrmFilePath = string.Empty;
            character.isReadOnly = false;
            return character;
        }

        /// <summary>
        /// アプリケーション設定をファイルに保存
        /// </summary>
        public void SaveAppSettings()
        {
            try
            {
                // 現在の設定からConfigSettingsオブジェクトを取得
                var settings = GetConfigSettings();

                // JSONにシリアライズ
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true, // 整形されたJSONを出力
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping // 日本語などの非ASCII文字をエスケープせずに出力
                };
                string json = JsonSerializer.Serialize(settings, options);

                // ファイルに保存
                File.WriteAllText(AppSettingsFilePath, json);

                Debug.WriteLine($"設定をファイルに保存しました: {AppSettingsFilePath}");

                // イベント発生
                SettingsSaved?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"アプリケーション設定ファイル保存エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// 全設定をファイルに保存
        /// </summary>
        public void SaveSettings()
        {
            SaveAppSettings();
            SaveAnimationSettings();
        }

        /// <summary>
        /// アニメーション設定をファイルから読み込む
        /// </summary>
        public void LoadAnimationSettings()
        {
            try
            {
                AnimationSettingsData? animationData = null;

                // 設定ファイルが存在するか確認
                if (File.Exists(AnimationSettingsFilePath))
                {
                    string json = File.ReadAllText(AnimationSettingsFilePath);
                    animationData = MessageHelper.DeserializeFromJson<AnimationSettingsData>(json);
                }

                // 設定ファイルがない場合やデシリアライズに失敗した場合は、デフォルト設定を読み込む
                if (animationData == null)
                {
                    animationData = LoadDefaultAnimationSettings();

                    if (animationData != null)
                    {
                        // デフォルト設定をファイルに保存
                        SaveAnimationSettingsData(animationData);
                        Debug.WriteLine($"デフォルトアニメーション設定をファイルに保存しました: {AnimationSettingsFilePath}");
                    }
                }

                // 読み込んだ設定を適用
                if (animationData != null)
                {
                    CurrentAnimationSettingIndex = animationData.currentAnimationSettingIndex;
                    AnimationSettings = new List<AnimationSetting>(animationData.animationSettings);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"アニメーション設定ファイル読み込みエラー: {ex.Message}");
                // エラーが発生した場合はデフォルト設定を使用
                var defaultData = LoadDefaultAnimationSettings();
                if (defaultData != null)
                {
                    CurrentAnimationSettingIndex = defaultData.currentAnimationSettingIndex;
                    AnimationSettings = new List<AnimationSetting>(defaultData.animationSettings);
                }
            }
        }

        /// <summary>
        /// アニメーション設定をファイルに保存
        /// </summary>
        public void SaveAnimationSettings()
        {
            try
            {
                var animationData = new AnimationSettingsData
                {
                    currentAnimationSettingIndex = CurrentAnimationSettingIndex,
                    animationSettings = new List<AnimationSetting>(AnimationSettings)
                };

                SaveAnimationSettingsData(animationData);
                Debug.WriteLine($"アニメーション設定をファイルに保存しました: {AnimationSettingsFilePath}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"アニメーション設定ファイル保存エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// デフォルトアニメーション設定を読み込む
        /// </summary>
        private AnimationSettingsData LoadDefaultAnimationSettings()
        {
            if (File.Exists(DefaultAnimationSettingsFilePath))
            {
                try
                {
                    string json = File.ReadAllText(DefaultAnimationSettingsFilePath);
                    var defaultData = MessageHelper.DeserializeFromJson<AnimationSettingsData>(json);

                    if (defaultData != null)
                    {
                        return defaultData;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"デフォルトアニメーション設定ファイル読み込みエラー: {ex.Message}");
                }
            }

            // 読み込みに失敗した場合は空の設定を返す
            return new AnimationSettingsData();
        }

        /// <summary>
        /// アニメーション設定データをファイルに保存する
        /// </summary>
        private void SaveAnimationSettingsData(AnimationSettingsData animationData)
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            string json = JsonSerializer.Serialize(animationData, options);
            File.WriteAllText(AnimationSettingsFilePath, json);
        }

        /// <summary>
        /// 現在選択されているキャラクター設定を取得
        /// </summary>
        /// <returns>現在のキャラクター設定、存在しない場合はnull</returns>
        public CharacterSettings? GetCurrentCharacter()
        {
            if (CharacterList == null || CharacterList.Count == 0)
                return null;

            if (CurrentCharacterIndex < 0 || CurrentCharacterIndex >= CharacterList.Count)
                return null;

            return CharacterList[CurrentCharacterIndex];
        }
    }

    /// <summary>
    /// アニメーション設定データクラス
    /// </summary>
    public class AnimationSettingsData
    {
        public int currentAnimationSettingIndex { get; set; } = 0;
        public List<AnimationSetting> animationSettings { get; set; } = new List<AnimationSetting>();
    }
}
