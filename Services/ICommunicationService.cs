using CocoroConsole.Communication;
using CocoroAI.Services;
using CocoroConsole.Models.OtomeKairoApi;
using System;
using System.Collections.Generic;
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
        event EventHandler<ChatRequest>? ChatMessageReceived;
        event EventHandler<ControlRequest>? ControlCommandReceived;
        event EventHandler<string>? ErrorOccurred;
        event EventHandler<StreamingChatEventArgs>? StreamingChatReceived;
        event EventHandler<bool>? ChatBusyChanged;
        event EventHandler<StatusUpdateEventArgs>? StatusUpdateRequested;
        event EventHandler<OtomeKairoStatus>? StatusChanged;
        event EventHandler<IReadOnlyList<LogMessage>>? LogMessagesReceived;
        event EventHandler<bool>? LogStreamConnectionChanged;
        event EventHandler<string>? LogStreamError;

        bool IsServerRunning { get; }
        OtomeKairoStatus CurrentStatus { get; }
        bool IsChatBusy { get; }

        Task StartServerAsync();
        Task StopServerAsync();
        ConfigSettings GetCurrentConfig();
        Task SendChatToCoreUnifiedAsync(string message, string? characterName = null, string? imageDataUrl = null);
        Task SendChatToCoreUnifiedAsync(string message, string? characterName = null, List<string>? imageDataUrls = null);
        void StartNewConversation();
        Task SendAnimationToShellAsync(string animationName);
        Task SendTTSStateToShellAsync(bool isUseTTS);
        Task StartLogStreamAsync();
        Task StopLogStreamAsync();
        void OpenLogViewer();
        Task<PositionResponse> GetShellPositionAsync();
        Task SendConfigPatchToShellAsync(Dictionary<string, object> updates);
        void RefreshSettingsCache();
        void NotifyOtomeKairoRestarting();
        void ResetShellConnectionState();
        Task RefreshOtomeKairoCurrentSettingsAsync();
        Task SetDesktopWatchEnabledAsync(bool enabled);
    }
}
