using CocoroConsole.Communication;
using CocoroConsole.Models.CocoroGhostApi;
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
        /// CocoroCoreポート
        /// </summary>
        int CocoroGhostPort { get; set; }

        /// <summary>
        /// CocoroShellポート
        /// </summary>
        int CocoroShellPort { get; set; }

        /// <summary>
        /// cocoro_ghost API Bearer トークン
        /// </summary>
        string CocoroGhostBearerToken { get; set; }

        /// <summary>
        /// キャラクター位置復元
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
        /// 逃げ先座標リスト
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
        /// キャラクターシャドウ
        /// </summary>
        int CharacterShadow { get; set; }

        /// <summary>
        /// キャラクターシャドウ解像度
        /// </summary>
        int CharacterShadowResolution { get; set; }

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
        /// 現在のキャラクターインデックス
        /// </summary>
        int CurrentCharacterIndex { get; set; }

        /// <summary>
        /// スクリーンショット設定
        /// </summary>
        ScreenshotSettings ScreenshotSettings { get; set; }

        /// <summary>
        /// マイク設定
        /// </summary>
        MicrophoneSettings MicrophoneSettings { get; set; }

        /// <summary>
        /// 定期コマンド実行設定
        /// </summary>
        Models.ScheduledCommandSettings ScheduledCommandSettings { get; set; }

        /// <summary>
        /// キャラクターリスト
        /// </summary>
        List<CharacterSettings> CharacterList { get; set; }

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
        /// cocoro_ghost APIから取得した設定をローカル設定に反映
        /// </summary>
        /// <param name="apiSettings">/settings のレスポンス</param>
        void ApplyCocoroGhostSettings(CocoroGhostSettings apiSettings);

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
        /// 現在選択されているキャラクター設定を取得
        /// </summary>
        /// <returns>現在のキャラクター設定、存在しない場合はnull</returns>
        CharacterSettings? GetCurrentCharacter();
    }
}
