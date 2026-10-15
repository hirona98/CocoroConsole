using System;
using System.Collections.Generic;

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
        public string personaId { get; set; } = string.Empty;
        public string personaDisplayName { get; set; } = string.Empty;
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
