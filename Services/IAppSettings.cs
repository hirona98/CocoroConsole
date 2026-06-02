using CocoroConsole.Communication;
using System.Collections.Generic;

namespace CocoroConsole.Services
{
    /// <summary>
    /// アプリケーション設定のインターフェース
    /// </summary>
    public interface IAppSettings
    {
        /// <summary>
        /// CocoroConsoleポート
        /// </summary>
        int CocoroConsolePort { get; set; }

        /// <summary>
        /// OtomeKairoポート
        /// </summary>
        int OtomeKairoPort { get; set; }

        /// <summary>
        /// OtomeKairo接続先ホスト
        /// </summary>
        string OtomeKairoHost { get; set; }

        /// <summary>
        /// 外部の OtomeKairo を使用するか
        /// </summary>
        bool UseExternalOtomeKairo { get; set; }

        /// <summary>
        /// CocoroShellポート
        /// </summary>
        int CocoroShellPort { get; set; }

        /// <summary>
        /// /api/events/stream の hello に使うクライアントID（安定ID）
        /// </summary>
        string ClientId { get; set; }

        /// <summary>
        /// otomekairo API Bearer トークン
        /// </summary>
        string OtomeKairoBearerToken { get; set; }

        /// <summary>
        /// 対話機能（LLM）を使用するか
        /// </summary>
        bool IsUseLLM { get; set; }

        /// <summary>
        /// アバター位置復元
        /// </summary>
        bool IsRestoreWindowPosition { get; set; }

        /// <summary>
        /// 最前面表示
        /// </summary>
        bool IsTopmost { get; set; }

        /// <summary>
        /// カーソル回避
        /// </summary>
        bool IsEscapeCursor { get; set; }

        /// <summary>
        /// 移動先座標リスト
        /// </summary>
        List<EscapePosition> EscapePositions { get; set; }

        /// <summary>
        /// 仮想キー入力
        /// </summary>
        bool IsInputVirtualKey { get; set; }

        /// <summary>
        /// 仮想キー文字列
        /// </summary>
        string VirtualKeyString { get; set; }

        /// <summary>
        /// 自動移動
        /// </summary>
        bool IsAutoMove { get; set; }

        /// <summary>
        /// 発話時メッセージウィンドウ表示
        /// </summary>
        bool ShowMessageWindow { get; set; }

        /// <summary>
        /// アンビエントオクルージョン有効
        /// </summary>
        bool IsEnableAmbientOcclusion { get; set; }

        /// <summary>
        /// MSAAレベル
        /// </summary>
        int MsaaLevel { get; set; }

        /// <summary>
        /// アバターシャドウ
        /// </summary>
        int AvatarShadow { get; set; }

        /// <summary>
        /// アバターシャドウ解像度
        /// </summary>
        int AvatarShadowResolution { get; set; }

        /// <summary>
        /// 背景シャドウ
        /// </summary>
        int BackgroundShadow { get; set; }

        /// <summary>
        /// 背景シャドウ解像度
        /// </summary>
        int BackgroundShadowResolution { get; set; }

        /// <summary>
        /// ウィンドウサイズ
        /// </summary>
        int WindowSize { get; set; }

        /// <summary>
        /// ウィンドウX座標
        /// </summary>
        float WindowPositionX { get; set; }

        /// <summary>
        /// ウィンドウY座標
        /// </summary>
        float WindowPositionY { get; set; }

        /// <summary>
        /// ウィンドウ位置一覧
        /// </summary>
        Dictionary<string, WindowPlacement> WindowPlacements { get; set; }

        /// <summary>
        /// 現在のアバターインデックス
        /// </summary>
        int CurrentAvatarIndex { get; set; }

        /// <summary>
        /// スクリーンショット設定
        /// </summary>
        ScreenshotSettings ScreenshotSettings { get; set; }

        /// <summary>
        /// マイク設定
        /// </summary>
        MicrophoneSettings MicrophoneSettings { get; set; }

        /// <summary>
        /// アバターリスト
        /// </summary>
        List<AvatarSettings> AvatarList { get; set; }

        /// <summary>
        /// 現在のアニメーション設定インデックス
        /// </summary>
        int CurrentAnimationSettingIndex { get; set; }

        /// <summary>
        /// アニメーション設定リスト
        /// </summary>
        List<AnimationSetting> AnimationSettings { get; set; }

        /// <summary>
        /// 設定が読み込まれたかどうか
        /// </summary>
        bool IsLoaded { get; set; }

        /// <summary>
        /// 設定値を更新
        /// </summary>
        /// <param name="config">サーバーから受信した設定値</param>
        void UpdateSettings(ConfigSettings config);

        /// <summary>
        /// 現在の設定からConfigSettingsオブジェクトを作成
        /// </summary>
        /// <returns>ConfigSettings オブジェクト</returns>
        ConfigSettings GetConfigSettings();

        /// <summary>
        /// 設定ファイルから設定を読み込む
        /// </summary>
        void LoadSettings();

        /// <summary>
        /// アプリケーション設定ファイルを読み込む
        /// </summary>
        void LoadAppSettings();

        /// <summary>
        /// アプリケーション設定をファイルに保存
        /// </summary>
        void SaveAppSettings();

        /// <summary>
        /// 全設定をファイルに保存
        /// </summary>
        void SaveSettings();

        /// <summary>
        /// アニメーション設定をファイルから読み込む
        /// </summary>
        void LoadAnimationSettings();

        /// <summary>
        /// アニメーション設定をファイルに保存
        /// </summary>
        void SaveAnimationSettings();

        /// <summary>
        /// ユーザーデータディレクトリを取得
        /// </summary>
        string UserDataDirectory { get; }
        /// <summary>
        /// OtomeKairo の HTTPS ベースURLを取得
        /// </summary>
        string GetOtomeKairoBaseUrl();

        /// <summary>
        /// OtomeKairo の WSS ベースURLを取得
        /// </summary>
        string GetOtomeKairoWebSocketBaseUrl();

        /// <summary>
        /// OtomeKairo 接続先がローカルかどうか
        /// </summary>
        bool IsOtomeKairoLocal();

        /// <summary>
        /// 現在選択されているアバター設定を取得
        /// </summary>
        /// <returns>現在のアバター設定、存在しない場合はnull</returns>
        AvatarSettings? GetCurrentAvatar();

        /// <summary>
        /// ウィンドウ位置を取得
        /// </summary>
        /// <param name="windowKey">ウィンドウ識別子</param>
        /// <returns>ウィンドウ位置。見つからない場合はnull</returns>
        WindowPlacement? GetWindowPlacement(string windowKey);

        /// <summary>
        /// ウィンドウ位置を更新
        /// </summary>
        /// <param name="windowKey">ウィンドウ識別子</param>
        /// <param name="left">X座標</param>
        /// <param name="top">Y座標</param>
        void SetWindowPlacement(string windowKey, double left, double top);
    }
}
