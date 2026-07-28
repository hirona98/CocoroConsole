using CocoroConsole.Communication;
using CocoroAI.Services;
using CocoroConsole.Models.OtomeKairoApi;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CocoroConsole.Services
{
    /// <summary>
    /// ステータス更新用のイベント引数
    /// </summary>
    public class StatusUpdateEventArgs : EventArgs
    {
        public bool IsConnected { get; }
        public string? Message { get; }

        public StatusUpdateEventArgs(bool isConnected, string? message = null)
        {
            IsConnected = isConnected;
            Message = message;
        }
    }

    /// <summary>
    /// 通信サービスのインターフェース
    /// </summary>
    public interface ICommunicationService : IDisposable
    {
        event EventHandler<UiMessageRequest>? UiMessageReceived;
        event EventHandler<ControlRequest>? ControlCommandReceived;
        event EventHandler<string>? ErrorOccurred;
        event EventHandler<ConversationOutputEventArgs>? ConversationOutputReceived;
        event EventHandler<bool>? ConversationInputBusyChanged;
        event EventHandler<StatusUpdateEventArgs>? StatusUpdateRequested;
        event EventHandler<OtomeKairoStatus>? StatusChanged;
        event EventHandler<IReadOnlyList<LogMessage>>? LogMessagesReceived;
        event EventHandler<bool>? LogStreamConnectionChanged;
        event EventHandler<string>? LogStreamError;

        bool IsServerRunning { get; }
        OtomeKairoStatus CurrentStatus { get; }
        bool IsConversationInputBusy { get; }

        Task StartServerAsync();
        Task StopServerAsync();
        ConfigSettings GetCurrentConfig();
        Task SendConversationInputToOtomeKairoAsync(
            string message,
            string? avatarName = null,
            string? imageDataUrl = null,
            string? speakerId = null,
            string? speakerDisplayName = null);
        Task SendConversationInputToOtomeKairoAsync(
            string message,
            string? avatarName = null,
            List<string>? imageDataUrls = null,
            string? speakerId = null,
            string? speakerDisplayName = null);
        void StartNewConversation();
        Task SendAnimationToShellAsync(string animationName);
        Task SendTTSStateToShellAsync(bool isUseTTS);
        Task StartLogStreamAsync();
        Task StopLogStreamAsync();
        void OpenLogViewer();
        Task<PositionResponse> GetShellPositionAsync();
        Task SendConfigPatchToShellAsync(Dictionary<string, object> updates);
        void RefreshSettingsCache();
        void ResetShellConnectionState();
        Task RefreshOtomeKairoCurrentSettingsAsync();
        Task SetDesktopWatchEnabledAsync(bool enabled);

        /// <summary>
        /// 現在の STT / TTS 設定を OtomeKairo に保存する。
        /// </summary>
        Task SaveAvatarSpeechSettingsAsync();

        /// <summary>
        /// 現在の CocoroConsole 端末設定を OtomeKairo に保存する。
        /// </summary>
        Task SaveConsoleClientSettingsAsync(CancellationToken cancellationToken = default);
    }
}
