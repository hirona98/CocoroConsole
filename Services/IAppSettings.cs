using CocoroConsole.Communication;
using CocoroConsole.Models.OtomeKairoApi;
using System.Collections.Generic;

namespace CocoroConsole.Services
{
    /// <summary>
    /// アプリケーション設定のインターフェース。
    /// 実行時の通常設定は OtomeKairo の文書をそのまま持つ。
    /// </summary>
    public interface IAppSettings
    {
        string ServerUrl { get; set; }

        List<string> ServerUrlHistory { get; }

        int CocoroConsolePort { get; set; }

        int CocoroShellPort { get; set; }

        string ClientId { get; set; }

        string ConversationDisplayName { get; }

        string? SelectedConversationDisplayNameId { get; set; }

        List<OtomeKairoConversationDisplayNameDefinition> ConversationDisplayNames { get; set; }

        string OtomeKairoBearerToken { get; set; }

        string ShellSessionToken { get; set; }

        OtomeKairoConsoleClientSettings ConsoleClient { get; set; }

        OtomeKairoCurrentSettings CurrentSettings { get; set; }

        OtomeKairoAvatarSpeechEditorState AvatarSpeech { get; set; }

        bool IsLoaded { get; set; }

        bool HasRemoteSettings { get; }

        void SaveVerifiedConnection(string serverUrl, string consoleAccessToken);

        ShellRuntimeConfig BuildShellRuntimeConfig();

        void ApplyRemoteSettings(
            OtomeKairoConsoleClientSettings consoleSettings,
            OtomeKairoCurrentSettings currentSettings,
            IReadOnlyList<OtomeKairoConversationDisplayNameDefinition> conversationDisplayNames,
            OtomeKairoAvatarSpeechEditorState avatarSpeech);

        void PrepareConsoleClientForSave();

        void PrepareAvatarSpeechForSave();

        void LoadSettings();

        void LoadAppSettings();

        void SaveAppSettings();

        void SaveSettings();

        string UserDataDirectory { get; }

        string GetOtomeKairoBaseUrl();

        string GetOtomeKairoWebSocketBaseUrl();

        OtomeKairoAvatarSpeechDefinition? GetCurrentAvatar();

        OtomeKairoConsoleAvatarPresentation? GetCurrentAvatarPresentation();

        OtomeKairoAvatarSpeechDefinition CreateAvatarFromDefaults(string displayName);

        OtomeKairoConsoleWindowPlacement? GetWindowPlacement(string windowKey);

        void SetWindowPlacement(string windowKey, double left, double top);
    }
}
