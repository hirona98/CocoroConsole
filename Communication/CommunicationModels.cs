using CocoroConsole.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CocoroConsole.Communication
{

    /// <summary>
    /// 対話メッセージペイロードクラス
    /// </summary>
    public class ChatMessagePayload
    {
        public string from { get; set; } = string.Empty;
        public string sessionId { get; set; } = string.Empty;
        public string message { get; set; } = string.Empty;
    }

    /// <summary>
    /// Style-Bert-VITS2の設定を保持するクラス
    /// </summary>
    public class VoicevoxConfig
    {
        public string endpointUrl { get; set; } = string.Empty;
        public int speakerId { get; set; }
        public float speedScale { get; set; }        // 話速 (0.5 - 2.0)
        public float pitchScale { get; set; }        // 音高 (-0.15 - 0.15)
        public float intonationScale { get; set; }   // 抑揚 (0.0 - 2.0)
        public float volumeScale { get; set; }       // 音量 (0.0 - 2.0)
        public float prePhonemeLength { get; set; }  // 音声の前の無音時間 (0.0 - 1.5)
        public float postPhonemeLength { get; set; } // 音声の後の無音時間 (0.0 - 1.5)
        public int outputSamplingRate { get; set; }  // 出力サンプリングレート
        public bool outputStereo { get; set; }       // ステレオ出力するか
    }

    public class StyleBertVits2Config
    {
        public string endpointUrl { get; set; } = string.Empty;
        public string modelName { get; set; } = string.Empty;
        public int modelId { get; set; }
        public string speakerName { get; set; } = string.Empty;
        public int speakerId { get; set; }
        public string style { get; set; } = string.Empty;
        public float styleWeight { get; set; }
        public float sdpRatio { get; set; }
        public float noise { get; set; }
        public float noiseW { get; set; }
        public float length { get; set; }
        public string language { get; set; } = string.Empty;
        public bool autoSplit { get; set; }
        public float splitInterval { get; set; }
        public string assistText { get; set; } = string.Empty;
        public float assistTextWeight { get; set; }
        public string referenceAudioPath { get; set; } = string.Empty;
    }

    /// <summary>
    /// AivisCloudの設定を保持するクラス
    /// </summary>
    public class AivisCloudConfig
    {
        public string apiKey { get; set; } = string.Empty;
        public string endpointUrl { get; set; } = string.Empty;
        public string modelUuid { get; set; } = string.Empty;
        public string speakerUuid { get; set; } = string.Empty;
        public int styleId { get; set; }
        public string styleName { get; set; } = string.Empty;
        public bool useSSML { get; set; }
        public string language { get; set; } = string.Empty;
        public float speakingRate { get; set; }
        public float emotionalIntensity { get; set; }
        public float tempoDynamics { get; set; }
        public float pitch { get; set; }
        public float volume { get; set; }
        public string outputFormat { get; set; } = string.Empty;
        public int outputBitrate { get; set; }
        public int outputSamplingRate { get; set; }
        public string outputAudioChannels { get; set; } = string.Empty;
    }

    public class AvatarSettings
    {
        // OtomeKairo のアバター資源と表示資源を結び付ける安定ID
        public string avatarId { get; set; } = string.Empty;
        public bool isReadOnly { get; set; }
        public string modelName { get; set; } = string.Empty;
        public string vrmFilePath { get; set; } = string.Empty;
        public bool isUseTTS { get; set; }

        public string ttsType { get; set; } = string.Empty; // "voicevox" or "style-bert-vits2" or "aivis-cloud"

        // TTS詳細設定
        public VoicevoxConfig voicevoxConfig { get; set; } = new VoicevoxConfig();
        public StyleBertVits2Config styleBertVits2Config { get; set; } = new StyleBertVits2Config();
        public AivisCloudConfig aivisCloudConfig { get; set; } = new AivisCloudConfig();

        public bool isUseSTT { get; set; } // STT（音声認識）機能の有効/無効
        public string sttEngine { get; set; } = string.Empty; // STTエンジン ("amivoice")
        public string sttProfileId { get; set; } = string.Empty; // AmiVoiceプロフィール名（マイページ登録の場合はサービスID）
        public string sttApiKey { get; set; } = string.Empty; // AmiVoice APPKEY（recognize の u）
        public bool isConvertMToon { get; set; } // UnlitをMToonに変換するかどうか
        public bool isEnableShadowOff { get; set; } // 影オフ機能の有効/無効
        public string shadowOffMesh { get; set; } = string.Empty; // 影を落とさないメッシュ名


        /// <summary>
        /// このAvatarSettingsオブジェクトのディープコピーを作成
        /// </summary>
        /// <returns>新しいAvatarSettingsインスタンス</returns>
        public AvatarSettings DeepCopy()
        {
            return new AvatarSettings
            {
                avatarId = this.avatarId,
                isReadOnly = this.isReadOnly,
                modelName = this.modelName,
                vrmFilePath = this.vrmFilePath,
                isUseTTS = this.isUseTTS,
                ttsType = this.ttsType,

                // VoicevoxConfigのディープコピー
                voicevoxConfig = new VoicevoxConfig
                {
                    endpointUrl = this.voicevoxConfig.endpointUrl,
                    speakerId = this.voicevoxConfig.speakerId,
                    speedScale = this.voicevoxConfig.speedScale,
                    pitchScale = this.voicevoxConfig.pitchScale,
                    intonationScale = this.voicevoxConfig.intonationScale,
                    volumeScale = this.voicevoxConfig.volumeScale,
                    prePhonemeLength = this.voicevoxConfig.prePhonemeLength,
                    postPhonemeLength = this.voicevoxConfig.postPhonemeLength,
                    outputSamplingRate = this.voicevoxConfig.outputSamplingRate,
                    outputStereo = this.voicevoxConfig.outputStereo
                },

                // StyleBertVits2Configのディープコピー
                styleBertVits2Config = new StyleBertVits2Config
                {
                    endpointUrl = this.styleBertVits2Config.endpointUrl,
                    modelName = this.styleBertVits2Config.modelName,
                    modelId = this.styleBertVits2Config.modelId,
                    speakerName = this.styleBertVits2Config.speakerName,
                    speakerId = this.styleBertVits2Config.speakerId,
                    style = this.styleBertVits2Config.style,
                    styleWeight = this.styleBertVits2Config.styleWeight,
                    sdpRatio = this.styleBertVits2Config.sdpRatio,
                    noise = this.styleBertVits2Config.noise,
                    noiseW = this.styleBertVits2Config.noiseW,
                    length = this.styleBertVits2Config.length,
                    language = this.styleBertVits2Config.language,
                    autoSplit = this.styleBertVits2Config.autoSplit,
                    splitInterval = this.styleBertVits2Config.splitInterval,
                    assistText = this.styleBertVits2Config.assistText,
                    assistTextWeight = this.styleBertVits2Config.assistTextWeight,
                    referenceAudioPath = this.styleBertVits2Config.referenceAudioPath
                },

                // AivisCloudConfigのディープコピー
                aivisCloudConfig = new AivisCloudConfig
                {
                    apiKey = this.aivisCloudConfig.apiKey,
                    endpointUrl = this.aivisCloudConfig.endpointUrl,
                    modelUuid = this.aivisCloudConfig.modelUuid,
                    speakerUuid = this.aivisCloudConfig.speakerUuid,
                    styleId = this.aivisCloudConfig.styleId,
                    styleName = this.aivisCloudConfig.styleName,
                    useSSML = this.aivisCloudConfig.useSSML,
                    language = this.aivisCloudConfig.language,
                    speakingRate = this.aivisCloudConfig.speakingRate,
                    emotionalIntensity = this.aivisCloudConfig.emotionalIntensity,
                    tempoDynamics = this.aivisCloudConfig.tempoDynamics,
                    pitch = this.aivisCloudConfig.pitch,
                    volume = this.aivisCloudConfig.volume,
                    outputFormat = this.aivisCloudConfig.outputFormat,
                    outputBitrate = this.aivisCloudConfig.outputBitrate,
                    outputSamplingRate = this.aivisCloudConfig.outputSamplingRate,
                    outputAudioChannels = this.aivisCloudConfig.outputAudioChannels
                },

                isUseSTT = this.isUseSTT,
                sttEngine = this.sttEngine,
                sttProfileId = this.sttProfileId,
                sttApiKey = this.sttApiKey,
                isConvertMToon = this.isConvertMToon,
                isEnableShadowOff = this.isEnableShadowOff,
                shadowOffMesh = this.shadowOffMesh
            };
        }
    }

    /// <summary>
    /// スクリーンショット設定クラス
    /// </summary>
    public class ScreenshotSettings
    {
        // スクリーンショット機能の有効/無効
        public bool enabled { get; set; } = false;

        // アクティブウィンドウのみキャプチャするか
        public bool captureActiveWindowOnly { get; set; } = true;

        // アイドルタイムアウト（分）。0 は無効。
        public int idleTimeoutMinutes { get; set; } = 10;

        // スクショ除外（ウィンドウタイトル正規表現）
        public List<string> excludePatterns { get; set; } = new List<string>();
    }

    /// <summary>
    /// マイク設定クラス
    /// </summary>
    public class MicrophoneSettings
    {
        public string inputSource { get; set; } = "local_microphone";
        public MicrophoneInputDevice? localInputDevice { get; set; }
        public ConsoleMicrophoneSettings? console { get; set; }
        public float vadProbabilityThreshold { get; set; } = 0.5f;
        public float speakerRecognitionThreshold { get; set; } = 0.6f;

        public MicrophoneSettings DeepCopy()
        {
            return new MicrophoneSettings
            {
                inputSource = inputSource,
                localInputDevice = localInputDevice?.DeepCopy(),
                console = console?.DeepCopy(),
                vadProbabilityThreshold = vadProbabilityThreshold,
                speakerRecognitionThreshold = speakerRecognitionThreshold,
            };
        }
    }

    /// <summary>
    /// OtomeKairo 動作端末のローカルマイク選択値。
    /// </summary>
    public class MicrophoneInputDevice
    {
        public string hostApi { get; set; } = string.Empty;
        public string name { get; set; } = string.Empty;

        public MicrophoneInputDevice DeepCopy()
        {
            return new MicrophoneInputDevice
            {
                hostApi = hostApi,
                name = name,
            };
        }
    }

    /// <summary>
    /// CocoroConsole のリモートマイク設定。
    /// </summary>
    public class ConsoleMicrophoneSettings
    {
        public string clientId { get; set; } = string.Empty;
        public ConsoleMicrophoneInputDevice? inputDevice { get; set; }

        public ConsoleMicrophoneSettings DeepCopy()
        {
            return new ConsoleMicrophoneSettings
            {
                clientId = clientId,
                inputDevice = inputDevice?.DeepCopy(),
            };
        }
    }

    /// <summary>
    /// CocoroConsole 動作端末の WASAPI 入力デバイス選択値。
    /// </summary>
    public class ConsoleMicrophoneInputDevice
    {
        public string deviceId { get; set; } = string.Empty;
        public string name { get; set; } = string.Empty;

        public ConsoleMicrophoneInputDevice DeepCopy()
        {
            return new ConsoleMicrophoneInputDevice
            {
                deviceId = deviceId,
                name = name,
            };
        }

        public override string ToString()
        {
            return name;
        }
    }

    /// <summary>
    /// メッセージウィンドウ設定クラス
    /// </summary>
    public class MessageWindowSettings
    {
        public int maxMessageCount { get; set; }
        public int maxTotalAvatars { get; set; }
        public float minWindowSize { get; set; }
        public float maxWindowSize { get; set; }
        public float fontSize { get; set; }
        public float horizontalOffset { get; set; }
        public float verticalOffset { get; set; }
    }

    /// <summary>
    /// 移動先座標設定クラス
    /// </summary>
    public class EscapePosition
    {
        public float x { get; set; } = 0f;
        public float y { get; set; } = 0f;
        public bool enabled { get; set; } = true;
    }

    /// <summary>
    /// 位置情報レスポンス
    /// </summary>
    public class PositionResponse
    {
        public string status { get; set; } = "success";
        public string message { get; set; } = string.Empty;
        public string timestamp { get; set; } = string.Empty;
        public PositionData position { get; set; } = new PositionData();
    }

    /// <summary>
    /// 位置情報データ
    /// </summary>
    public class PositionData
    {
        public float x { get; set; } = 0f;
        public float y { get; set; } = 0f;
        public SizeData windowSize { get; set; } = new SizeData();
    }

    /// <summary>
    /// サイズ情報データ
    /// </summary>
    public class SizeData
    {
        public float width { get; set; } = 0f;
        public float height { get; set; } = 0f;
    }

    /// <summary>
    /// ウィンドウ位置データ
    /// </summary>
    public class WindowPlacement
    {
        public double left { get; set; }
        public double top { get; set; }
    }

    /// <summary>
    /// アプリケーション設定クラス
    /// </summary>
    public class ConfigSettings
    {
        public int CocoroConsolePort { get; set; }
        public int otomeKairoPort { get; set; }
        // true: 外部の OtomeKairo に接続する / false: ローカル起動を前提に接続する
        public bool? useExternalOtomeKairo { get; set; }
        // OtomeKairo 接続先ホスト（例: 127.0.0.1 / localhost / 192.168.1.50）
        public string otomeKairoHost { get; set; } = "127.0.0.1";
        public int cocoroShellPort { get; set; }
        // /api/events/stream で hello を送るためのクライアントID（安定ID）
        public string clientId { get; set; } = string.Empty;
        // テキスト入力で participants[].display_name に渡す呼び名
        public string conversationDisplayName { get; set; } = string.Empty;
        public string? otomeKairoBearerToken { get; set; }
        public bool isRestoreWindowPosition { get; set; }
        public bool isTopmost { get; set; }
        public bool isEscapeCursor { get; set; }
        public List<EscapePosition> escapePositions { get; set; } = new List<EscapePosition>();
        public bool isInputVirtualKey { get; set; }
        public string virtualKeyString { get; set; } = string.Empty;
        public bool isAutoMove { get; set; }
        public bool showMessageWindow { get; set; }
        public bool isEnableAmbientOcclusion { get; set; }
        public int msaaLevel { get; set; }
        public int avatarShadow { get; set; }
        public int avatarShadowResolution { get; set; }
        public int backgroundShadow { get; set; }
        public int backgroundShadowResolution { get; set; }
        public float windowSize { get; set; }
        public float windowPositionX { get; set; }
        public float windowPositionY { get; set; }
        public ScreenshotSettings screenshotSettings { get; set; } = new ScreenshotSettings();
        public MicrophoneSettings microphoneSettings { get; set; } = new MicrophoneSettings();
        public MessageWindowSettings messageWindowSettings { get; set; } = new MessageWindowSettings();
        public Dictionary<string, WindowPlacement> windowPlacements { get; set; } = new Dictionary<string, WindowPlacement>();

        public int currentAvatarIndex { get; set; }
        public List<AvatarSettings> avatarList { get; set; } = new List<AvatarSettings>();

        /// <summary>
        /// このConfigSettingsオブジェクトのディープコピーを作成
        /// </summary>
        /// <returns>新しいConfigSettingsインスタンス</returns>
        public ConfigSettings DeepCopy()
        {
            return new ConfigSettings
            {
                CocoroConsolePort = this.CocoroConsolePort,
                otomeKairoPort = this.otomeKairoPort,
                useExternalOtomeKairo = this.useExternalOtomeKairo,
                otomeKairoHost = this.otomeKairoHost,
                cocoroShellPort = this.cocoroShellPort,
                clientId = this.clientId,
                conversationDisplayName = this.conversationDisplayName,
                otomeKairoBearerToken = this.otomeKairoBearerToken,
                isRestoreWindowPosition = this.isRestoreWindowPosition,
                isTopmost = this.isTopmost,
                isEscapeCursor = this.isEscapeCursor,
                isInputVirtualKey = this.isInputVirtualKey,
                virtualKeyString = this.virtualKeyString,
                isAutoMove = this.isAutoMove,
                showMessageWindow = this.showMessageWindow,
                isEnableAmbientOcclusion = this.isEnableAmbientOcclusion,
                msaaLevel = this.msaaLevel,
                avatarShadow = this.avatarShadow,
                avatarShadowResolution = this.avatarShadowResolution,
                backgroundShadow = this.backgroundShadow,
                backgroundShadowResolution = this.backgroundShadowResolution,
                windowSize = this.windowSize,
                windowPositionX = this.windowPositionX,
                windowPositionY = this.windowPositionY,
                currentAvatarIndex = this.currentAvatarIndex,

                // 複雑オブジェクトのディープコピー
                screenshotSettings = new ScreenshotSettings
                {
                    enabled = this.screenshotSettings.enabled,
                    captureActiveWindowOnly = this.screenshotSettings.captureActiveWindowOnly,
                    idleTimeoutMinutes = this.screenshotSettings.idleTimeoutMinutes,
                    excludePatterns = new List<string>(this.screenshotSettings.excludePatterns)
                },

                microphoneSettings = this.microphoneSettings.DeepCopy(),

                messageWindowSettings = new MessageWindowSettings
                {
                    maxMessageCount = this.messageWindowSettings.maxMessageCount,
                    maxTotalAvatars = this.messageWindowSettings.maxTotalAvatars,
                    minWindowSize = this.messageWindowSettings.minWindowSize,
                    maxWindowSize = this.messageWindowSettings.maxWindowSize,
                    fontSize = this.messageWindowSettings.fontSize,
                    horizontalOffset = this.messageWindowSettings.horizontalOffset,
                    verticalOffset = this.messageWindowSettings.verticalOffset
                },

                // ウィンドウ位置情報のディープコピー
                windowPlacements = this.windowPlacements.ToDictionary(
                    kvp => kvp.Key,
                    kvp => new WindowPlacement
                    {
                        left = kvp.Value.left,
                        top = kvp.Value.top
                    }),

                // EscapePositionリストのディープコピー
                escapePositions = this.escapePositions.Select(ep => new EscapePosition
                {
                    x = ep.x,
                    y = ep.y,
                    enabled = ep.enabled
                }).ToList(),

                // AvatarSettingsリストのディープコピー
                avatarList = this.avatarList.Select(c => c.DeepCopy()).ToList()
            };
        }
    }


    /// <summary>
    /// アニメーション設定クラス
    /// </summary>
    public class AnimationSetting
    {
        // OtomeKairo のアニメーションセットを識別する安定ID
        public string animationSetId { get; set; } = string.Empty;
        public string animeSetName { get; set; } = "デフォルト"; // 設定セット名
        public int postureChangeLoopCountStanding { get; set; } = 30; // 立ち姿勢の変更ループ回数
        public int postureChangeLoopCountSittingFloor { get; set; } = 30; // 座り姿勢の変更ループ回数
        public List<AnimationConfig> animations { get; set; } = new List<AnimationConfig>(); // 個別アニメーション設定
    }

    /// <summary>
    /// 個別アニメーション設定クラス
    /// </summary>
    public class AnimationConfig
    {
        public string displayName { get; set; } = ""; // UI表示名（例：「立ち_手を振る」）
        public int animationType { get; set; } = 0; // 0:Standing, 1:SittingFloor (2:LyingDownは非表示)
        public string animationName { get; set; } = ""; // Animator内での名前（例：「DT_01_wait_natural_F_001_FBX」）
        public bool isEnabled { get; set; } = true; // 有効/無効
    }

    /// <summary>
    /// CocoroShell が起動時に取得する実行用設定。
    /// OtomeKairo への接続資格や CocoroConsole 専用設定を含めません。
    /// </summary>
    public class ShellRuntimeConfig
    {
        public string clientId { get; set; } = string.Empty;
        public int shellApiPort { get; set; }
        public ShellDisplaySettings display { get; set; } = new ShellDisplaySettings();
        public ShellAvatarSettings avatar { get; set; } = new ShellAvatarSettings();
        public ShellMotionSettings motion { get; set; } = new ShellMotionSettings();
    }

    /// <summary>
    /// CocoroShell が表示に使用する選択済みアバター設定。
    /// </summary>
    public class ShellAvatarSettings
    {
        public string avatarId { get; set; } = string.Empty;
        public bool isReadOnly { get; set; }
        public string modelName { get; set; } = string.Empty;
        public string vrmFilePath { get; set; } = string.Empty;
        public bool isConvertMToon { get; set; }
        public bool isEnableShadowOff { get; set; }
        public string shadowOffMesh { get; set; } = string.Empty;
    }

    /// <summary>
    /// CocoroShell の表示処理に必要な設定。
    /// </summary>
    public class ShellDisplaySettings
    {
        public bool restoreWindowPosition { get; set; }
        public bool topmost { get; set; }
        public bool escapeCursor { get; set; }
        public List<EscapePosition> escapePositions { get; set; } = new List<EscapePosition>();
        public bool touchVirtualKeyEnabled { get; set; }
        public string virtualKey { get; set; } = string.Empty;
        public bool autoMove { get; set; }
        public bool showMessageWindow { get; set; }
        public MessageWindowSettings messageWindow { get; set; } = new MessageWindowSettings();
        public bool ambientOcclusionEnabled { get; set; }
        public float avatarWindowSize { get; set; }
        public float avatarPositionX { get; set; }
        public float avatarPositionY { get; set; }
        public int msaaLevel { get; set; }
        public int avatarShadowMode { get; set; }
        public int avatarShadowResolution { get; set; }
        public int backgroundShadowMode { get; set; }
        public int backgroundShadowResolution { get; set; }
    }

    /// <summary>
    /// CocoroShell が使用するモーション設定。
    /// </summary>
    public class ShellMotionSettings
    {
        public string selectedAnimationSetId { get; set; } = string.Empty;
        public List<AnimationSetting> animationSettings { get; set; } = new List<AnimationSetting>();
    }

    /// <summary>
    /// CocoroShell が確定したアバター位置。
    /// </summary>
    public class ShellAvatarPositionRequest
    {
        public float x { get; set; }
        public float y { get; set; }
    }

    #region REST API ペイロードクラス

    /// <summary>
    /// CocoroConsole API: UIメッセージ要求
    /// </summary>
    public class UiMessageRequest
    {
        public string memoryId { get; set; } = string.Empty;
        public string sessionId { get; set; } = string.Empty;
        public string message { get; set; } = string.Empty;
        public string role { get; set; } = string.Empty; // "user" | "assistant"
        public string content { get; set; } = string.Empty;
        public string sourceKind { get; set; } = string.Empty;
        public bool forceNewBubble { get; set; }
        public DateTime timestamp { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// CocoroConsole API: 制御コマンドリクエスト
    /// </summary>
    public class ControlRequest
    {
        public string action { get; set; } = string.Empty; // "shutdown" | "restart"
        public Dictionary<string, object>? @params { get; set; }
        public string? reason { get; set; }
    }

    /// <summary>
    /// 標準レスポンス
    /// </summary>
    public class StandardResponse
    {
        public string status { get; set; } = "success"; // "success" | "error"
        public string message { get; set; } = string.Empty;
        public DateTime timestamp { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// エラーレスポンス
    /// </summary>
    public class ErrorResponse
    {
        public string status { get; set; } = "error";
        public string message { get; set; } = string.Empty;
        public string? errorCode { get; set; }
        public DateTime timestamp { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// CocoroShell API: アニメーションリクエスト
    /// </summary>
    public class AnimationRequest
    {
        public string animationName { get; set; } = string.Empty;
    }

    /// <summary>
    /// CocoroShell API: 制御コマンドリクエスト
    /// </summary>
    public class ShellControlRequest
    {
        public string action { get; set; } = string.Empty;
        public Dictionary<string, object>? @params { get; set; }
    }

    /// <summary>
    /// CocoroConsole API: ステータス更新リクエスト
    /// </summary>
    public class StatusUpdateRequest
    {
        public string message { get; set; } = string.Empty; // ステータスメッセージ
        public string? type { get; set; } // ステータスタイプ（"api_start", "api_end"など）
        public DateTime timestamp { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// ヘルスチェックレスポンス
    /// </summary>
    public class HealthCheckResponse
    {
        public string status { get; set; } = "healthy";
    }

    /// <summary>
    /// ログメッセージ
    /// </summary>
    public class LogMessage
    {
        public DateTime timestamp { get; set; } = DateTime.UtcNow;
        public string level { get; set; } = string.Empty; // "DEBUG", "INFO", "WARNING", "ERROR"
        public string component { get; set; } = string.Empty; // "OtomeKairo"
        public string message { get; set; } = string.Empty;
    }

    /// <summary>
    /// 対話出力イベントデータ
    /// </summary>
    public class ConversationOutputEventArgs : EventArgs
    {
        public string Content { get; set; } = string.Empty; // 出力内容
        public bool IsFinished { get; set; } // 完了フラグ
        public string? ErrorMessage { get; set; } // エラーメッセージ（エラー時のみ）
        public bool IsError { get; set; } // エラーフラグ
    }

    #endregion
}
