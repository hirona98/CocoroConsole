using CocoroConsole.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CocoroConsole.Communication
{

    /// <summary>
    /// チャットメッセージペイロードクラス
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

    public class CharacterSettings
    {
        public bool isReadOnly { get; set; }
        public string modelName { get; set; } = string.Empty;
        public string vrmFilePath { get; set; } = string.Empty;
        public bool isUseTTS { get; set; }

        public string ttsType { get; set; } = string.Empty; // "voicevox" or "style-bert-vits2" or "aivis-cloud"

        // TTS詳細設定
        public VoicevoxConfig voicevoxConfig { get; set; } = new VoicevoxConfig();
        public StyleBertVits2Config styleBertVits2Config { get; set; } = new StyleBertVits2Config();
        public AivisCloudConfig aivisCloudConfig { get; set; } = new AivisCloudConfig();

        public bool isEnableMemory { get; set; } // メモリ機能の有効/無効
        public bool isUseSTT { get; set; } // STT（音声認識）機能の有効/無効
        public string sttEngine { get; set; } = string.Empty; // STTエンジン ("amivoice" | "openai")
        public string sttWakeWord { get; set; } = string.Empty; // STT起動ワード
        public string sttApiKey { get; set; } = string.Empty; // STT用APIキー
        public string sttLanguage { get; set; } = string.Empty; // STT言語設定
        public bool isConvertMToon { get; set; } // UnlitをMToonに変換するかどうか
        public bool isEnableShadowOff { get; set; } // 影オフ機能の有効/無効
        public string shadowOffMesh { get; set; } = string.Empty; // 影を落とさないメッシュ名


        /// <summary>
        /// このCharacterSettingsオブジェクトのディープコピーを作成
        /// </summary>
        /// <returns>新しいCharacterSettingsインスタンス</returns>
        public CharacterSettings DeepCopy()
        {
            return new CharacterSettings
            {
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

                isEnableMemory = this.isEnableMemory,
                isUseSTT = this.isUseSTT,
                sttEngine = this.sttEngine,
                sttWakeWord = this.sttWakeWord,
                sttApiKey = this.sttApiKey,
                sttLanguage = this.sttLanguage,
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
        public bool enabled { get; set; }
        public int intervalMinutes { get; set; }
        public bool captureActiveWindowOnly { get; set; }
        public int idleTimeoutMinutes { get; set; }
        public List<string> excludePatterns { get; set; } = new List<string>();
    }

    /// <summary>
    /// マイク設定クラス
    /// </summary>
    public class MicrophoneSettings
    {
        public int inputThreshold { get; set; }
        public float speakerRecognitionThreshold { get; set; }
    }

    /// <summary>
    /// メッセージウィンドウ設定クラス
    /// </summary>
    public class MessageWindowSettings
    {
        public int maxMessageCount { get; set; }
        public int maxTotalCharacters { get; set; }
        public float minWindowSize { get; set; }
        public float maxWindowSize { get; set; }
        public float fontSize { get; set; }
        public float horizontalOffset { get; set; }
        public float verticalOffset { get; set; }
    }

    /// <summary>
    /// 逃げ先座標設定クラス
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
    /// アプリケーション設定クラス
    /// </summary>
    public class ConfigSettings
    {
        public int CocoroConsolePort { get; set; }
        public int cocoroCorePort { get; set; }
        public int cocoroShellPort { get; set; }
        public string? cocoroGhostBearerToken { get; set; }
        public bool isUseLLM { get; set; }
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
        public int characterShadow { get; set; }
        public int characterShadowResolution { get; set; }
        public int backgroundShadow { get; set; }
        public int backgroundShadowResolution { get; set; }
        public float windowSize { get; set; }
        public float windowPositionX { get; set; }
        public float windowPositionY { get; set; }
        public ScreenshotSettings screenshotSettings { get; set; } = new ScreenshotSettings();
        public MicrophoneSettings microphoneSettings { get; set; } = new MicrophoneSettings();
        public MessageWindowSettings messageWindowSettings { get; set; } = new MessageWindowSettings();
        public ScheduledCommandSettings scheduledCommandSettings { get; set; } = new ScheduledCommandSettings();

        public int currentCharacterIndex { get; set; }
        public List<CharacterSettings> characterList { get; set; } = new List<CharacterSettings>();

        /// <summary>
        /// このConfigSettingsオブジェクトのディープコピーを作成
        /// </summary>
        /// <returns>新しいConfigSettingsインスタンス</returns>
        public ConfigSettings DeepCopy()
        {
            return new ConfigSettings
            {
                CocoroConsolePort = this.CocoroConsolePort,
                cocoroCorePort = this.cocoroCorePort,
                cocoroShellPort = this.cocoroShellPort,
                cocoroGhostBearerToken = this.cocoroGhostBearerToken,
                isUseLLM = this.isUseLLM,
                isRestoreWindowPosition = this.isRestoreWindowPosition,
                isTopmost = this.isTopmost,
                isEscapeCursor = this.isEscapeCursor,
                isInputVirtualKey = this.isInputVirtualKey,
                virtualKeyString = this.virtualKeyString,
                isAutoMove = this.isAutoMove,
                showMessageWindow = this.showMessageWindow,
                isEnableAmbientOcclusion = this.isEnableAmbientOcclusion,
                msaaLevel = this.msaaLevel,
                characterShadow = this.characterShadow,
                characterShadowResolution = this.characterShadowResolution,
                backgroundShadow = this.backgroundShadow,
                backgroundShadowResolution = this.backgroundShadowResolution,
                windowSize = this.windowSize,
                windowPositionX = this.windowPositionX,
                windowPositionY = this.windowPositionY,
                currentCharacterIndex = this.currentCharacterIndex,

                // 複雑オブジェクトのディープコピー
                screenshotSettings = new ScreenshotSettings
                {
                    enabled = this.screenshotSettings.enabled,
                    intervalMinutes = this.screenshotSettings.intervalMinutes,
                    captureActiveWindowOnly = this.screenshotSettings.captureActiveWindowOnly,
                    idleTimeoutMinutes = this.screenshotSettings.idleTimeoutMinutes,
                    excludePatterns = new List<string>(this.screenshotSettings.excludePatterns)
                },

                scheduledCommandSettings = new ScheduledCommandSettings
                {
                    Enabled = this.scheduledCommandSettings.Enabled,
                    Command = this.scheduledCommandSettings.Command,
                    IntervalMinutes = this.scheduledCommandSettings.IntervalMinutes
                },

                microphoneSettings = new MicrophoneSettings
                {
                    inputThreshold = this.microphoneSettings.inputThreshold,
                    speakerRecognitionThreshold = this.microphoneSettings.speakerRecognitionThreshold
                },

                messageWindowSettings = new MessageWindowSettings
                {
                    maxMessageCount = this.messageWindowSettings.maxMessageCount,
                    maxTotalCharacters = this.messageWindowSettings.maxTotalCharacters,
                    minWindowSize = this.messageWindowSettings.minWindowSize,
                    maxWindowSize = this.messageWindowSettings.maxWindowSize,
                    fontSize = this.messageWindowSettings.fontSize,
                    horizontalOffset = this.messageWindowSettings.horizontalOffset,
                    verticalOffset = this.messageWindowSettings.verticalOffset
                },

                // EscapePositionリストのディープコピー
                escapePositions = this.escapePositions.Select(ep => new EscapePosition
                {
                    x = ep.x,
                    y = ep.y,
                    enabled = ep.enabled
                }).ToList(),

                // CharacterSettingsリストのディープコピー
                characterList = this.characterList.Select(c => c.DeepCopy()).ToList()
            };
        }
    }


    /// <summary>
    /// アニメーション設定クラス
    /// </summary>
    public class AnimationSetting
    {
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

    #region REST API ペイロードクラス

    /// <summary>
    /// CocoroConsole API: チャットリクエスト
    /// </summary>
    public class ChatRequest
    {
        public string memoryId { get; set; } = string.Empty;
        public string sessionId { get; set; } = string.Empty;
        public string message { get; set; } = string.Empty;
        public string role { get; set; } = string.Empty; // "user" | "assistant"
        public string content { get; set; } = string.Empty;
        public DateTime timestamp { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// CocoroConsole API: 制御コマンドリクエスト
    /// </summary>
    public class ControlRequest
    {
        public string action { get; set; } = string.Empty; // "shutdown" | "restart" | "reloadConfig"
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
    /// CocoroShell API: チャットリクエスト
    /// </summary>
    public class ShellChatRequest
    {
        public string content { get; set; } = string.Empty;
        public VoiceParams? voiceParams { get; set; }
        public string? animation { get; set; } // "talk" | "idle" | null
        public string? characterName { get; set; }
    }

    /// <summary>
    /// 音声パラメータ
    /// </summary>
    public class VoiceParams
    {
        public int speaker_id { get; set; } = 1;
        public float speed { get; set; } = 1.0f;
        public float pitch { get; set; } = 0.0f;
        public float volume { get; set; } = 1.0f;
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
    /// CocoroShell API: 設定部分更新リクエスト
    /// </summary>
    public class ConfigPatchRequest
    {
        public Dictionary<string, object> updates { get; set; } = new Dictionary<string, object>();
        public string[] changedFields { get; set; } = Array.Empty<string>();
    }

    /// <summary>
    /// CocoroCore API: 制御コマンドリクエスト
    /// </summary>
    public class CoreControlRequest
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
        public string component { get; set; } = string.Empty; // "CocoroCore"
        public string message { get; set; } = string.Empty;
    }


    // ========================================
    // CocoroGhost チャットAPI関連モデル
    // ========================================

    /// <summary>
    /// CocoroGhost 画像データ
    /// </summary>
    public class ImageData
    {
        public string data { get; set; } = string.Empty; // Base64 data URL形式の画像データ
    }

    /// <summary>
    /// CocoroGhost 通知データ
    /// </summary>
    public class NotificationData
    {
        public string original_source { get; set; } = string.Empty; // 通知送信元
        public string original_message { get; set; } = string.Empty; // 元の通知メッセージ
    }

    /// <summary>
    /// CocoroGhost 会話履歴メッセージ
    /// </summary>
    public class HistoryMessage
    {
        public string role { get; set; } = string.Empty; // "user" | "assistant"
        public string content { get; set; } = string.Empty; // メッセージ内容
        public string timestamp { get; set; } = string.Empty; // メッセージ時刻（ISO形式）
    }

    /// <summary>
    /// ストリーミングチャットイベントデータ
    /// </summary>
    public class StreamingChatEventArgs : EventArgs
    {
        public string Content { get; set; } = string.Empty; // ストリーミングコンテンツ
        public bool IsFinished { get; set; } // 完了フラグ
        public string? ErrorMessage { get; set; } // エラーメッセージ（エラー時のみ）
        public bool IsError { get; set; } // エラーフラグ
    }

    #endregion
}
