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
    /// OtomeKairo で確定した音声会話入力を表します。
    /// </summary>
    public class VoiceConversationInputEventArgs : EventArgs
    {
        public int? UtteranceSeq { get; init; }
        public string MessageId { get; init; } = string.Empty;
        public string SourceClientId { get; init; } = string.Empty;
        public string SourceKind { get; init; } = string.Empty;
        public string Message { get; init; } = string.Empty;
        public string InteractionRef { get; init; } = string.Empty;
        public string SpeakerRef { get; init; } = string.Empty;
        public IReadOnlyList<string> ParticipantRefs { get; init; } = Array.Empty<string>();
        public string DisplayName { get; init; } = string.Empty;
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
        event EventHandler<VoiceConversationInputEventArgs>? VoiceConversationInputReceived;
        event EventHandler<OtomeKairoAudioRuntimeState>? AudioRuntimeStateChanged;
        event EventHandler<bool>? EventsStreamConnectionChanged;

        bool IsServerRunning { get; }
        OtomeKairoStatus CurrentStatus { get; }
        bool IsConversationInputBusy { get; }

        Task StartServerAsync();
        Task PrepareShellRuntimeAsync();
        Task StopServerAsync();
        ConfigSettings GetCurrentConfig();
        Task SendConversationInputToOtomeKairoAsync(
            string messageId,
            string message,
            List<string>? imageDataUrls = null,
            string? speakerId = null,
            string? speakerDisplayName = null);
        void StartNewConversation();
        Task SendAnimationToShellAsync(string animationName);
        Task StartLogStreamAsync();
        Task StopLogStreamAsync();
        void OpenLogViewer();
        Task<PositionResponse> GetShellPositionAsync();
        void RefreshSettingsCache();
        Task RefreshOtomeKairoCurrentSettingsAsync();
        Task SetDesktopWatchEnabledAsync(bool enabled);

        /// <summary>
        /// 現在の STT / TTS 設定を OtomeKairo に保存する。
        /// </summary>
        Task SaveAvatarSpeechSettingsAsync();

        /// <summary>
        /// 選択中アバターの STT 運用トグルだけを更新する。
        /// </summary>
        Task SetSttEnabledAsync(bool enabled);

        /// <summary>
        /// 選択中アバターの TTS 運用トグルだけを更新する。
        /// </summary>
        Task SetTtsEnabledAsync(bool enabled);

        /// <summary>
        /// 現在の CocoroConsole 端末設定を OtomeKairo に保存する。
        /// </summary>
        Task SaveConsoleClientSettingsAsync(CancellationToken cancellationToken = default);
    }
}
