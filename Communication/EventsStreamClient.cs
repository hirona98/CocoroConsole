using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using CocoroConsole.Models.OtomeKairoApi;

namespace CocoroConsole.Communication
{
    /// <summary>
    /// OtomeKairo の /api/events/stream に接続してサーバー駆動のイベントと制御要求を受信するクライアント。
    /// </summary>
    public sealed class EventsStreamClient : ResilientWebSocketClientBase
    {
        private readonly string? _clientId;
        private readonly IReadOnlyList<OtomeKairoCapabilityOffer>? _caps;
        private readonly IReadOnlyList<OtomeKairoVisionSourceOffer>? _visionSources;
        private OtomeKairoEventData? _pendingAssistantAudio;

        public event EventHandler<OtomeKairoEvent>? EventReceived;
        public event EventHandler<OtomeKairoAssistantAudioReceivedEventArgs>? AssistantAudioReceived;
        public event EventHandler<bool>? ConnectionStateChanged;
        public event EventHandler<string>? ErrorOccurred;

        public EventsStreamClient(
            Uri webSocketUri,
            string bearerToken,
            string? clientId = null,
            IReadOnlyList<OtomeKairoCapabilityOffer>? caps = null,
            IReadOnlyList<OtomeKairoVisionSourceOffer>? visionSources = null)
            : base(webSocketUri, bearerToken)
        {
            _clientId = string.IsNullOrWhiteSpace(clientId) ? null : clientId.Trim();
            _caps = caps;
            _visionSources = visionSources;
        }

        protected override string ConnectFailureLabel => "イベントストリーム接続失敗";
        protected override string ReceiveFailureLabel => "イベントストリーム受信エラー";
        protected override string AuthenticationFailureMessage => "イベントストリーム認証エラーのため再接続を停止しました。";

        protected override void RaiseConnectionStateChanged(bool isConnected)
        {
            ConnectionStateChanged?.Invoke(this, isConnected);
        }

        protected override void RaiseError(string message)
        {
            ErrorOccurred?.Invoke(this, message);
        }

        protected override async Task OnConnectedAsync(CancellationToken cancellationToken)
        {
            if (WebSocket == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(_clientId))
            {
                return;
            }

            try
            {
                var payload = new HelloMessage
                {
                    Type = "hello",
                    ClientId = _clientId!,
                    ClientKind = "cocoro_console",
                    Caps = _caps?.ToArray() ?? new[] { new OtomeKairoCapabilityOffer("vision.capture", "1") },
                    EventSubscriptions = new[]
                    {
                        "conversation_input",
                        "assistant_message",
                        "assistant_audio",
                        "audio_runtime_state",
                    },
                    VisionSources = _visionSources?.ToArray() ?? Array.Empty<OtomeKairoVisionSourceOffer>(),
                };

                var json = JsonSerializer.Serialize(payload);
                await SendTextAsync(json, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[EventsStream] hello send error: {ex.Message}");
            }
        }

        protected override void HandleTextMessage(string json)
        {
            HandleMessage(json);
        }

        protected override void HandleBinaryMessage(byte[] payload)
        {
            var metadata = _pendingAssistantAudio
                ?? throw new InvalidOperationException("assistant_audio metadataより先にbinary messageを受信しました。");
            _pendingAssistantAudio = null;
            if (!string.Equals(metadata.Status, "succeeded", StringComparison.Ordinal) ||
                !string.Equals(metadata.MediaType, "audio/wav", StringComparison.Ordinal) ||
                metadata.ByteCount != payload.Length)
            {
                throw new InvalidOperationException("assistant_audio binaryがmetadataと一致しません。");
            }
            AssistantAudioReceived?.Invoke(
                this,
                new OtomeKairoAssistantAudioReceivedEventArgs(metadata, payload));
        }

        private void HandleMessage(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.ValueKind == JsonValueKind.Array)
                {
                    foreach (var element in root.EnumerateArray())
                    {
                        if (TryParseEvent(element, out var ev))
                        {
                            PrepareAssistantAudio(ev);
                            EventReceived?.Invoke(this, ev);
                        }
                    }
                    return;
                }

                if (TryParseEvent(root, out var singleEvent))
                {
                    PrepareAssistantAudio(singleEvent);
                    EventReceived?.Invoke(this, singleEvent);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[EventsStream] JSON parse error: {ex.Message}");
                ErrorOccurred?.Invoke(this, $"イベントパースエラー: {ex.Message}");
            }
        }

        private void PrepareAssistantAudio(OtomeKairoEvent ev)
        {
            if (!string.Equals(ev.Type, "assistant_audio", StringComparison.Ordinal))
            {
                return;
            }
            _pendingAssistantAudio = string.Equals(ev.Data.Status, "succeeded", StringComparison.Ordinal)
                ? ev.Data
                : null;
        }

        private static bool TryParseEvent(JsonElement element, out OtomeKairoEvent ev)
        {
            ev = new OtomeKairoEvent();

            if (element.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            try
            {
                var type = element.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;
                if (string.IsNullOrWhiteSpace(type))
                {
                    return false;
                }

                // --- event_id（otomekairo の events.event_id、命令は 0） ---
                if (!element.TryGetProperty("event_id", out var eventIdElement))
                {
                    return false;
                }
                if (!eventIdElement.TryGetInt32(out var eventId))
                {
                    return false;
                }

                var data = new OtomeKairoEventData();
                if (element.TryGetProperty("data", out var dataElement) && dataElement.ValueKind == JsonValueKind.Object)
                {
                    data.SystemText = dataElement.TryGetProperty("system_text", out var systemText) ? systemText.GetString() : null;
                    data.Message = dataElement.TryGetProperty("message", out var message) ? message.GetString() : null;
                    data.MessageId = dataElement.TryGetProperty("message_id", out var messageId) ? messageId.GetString() : null;
                    data.CreatedAt = dataElement.TryGetProperty("created_at", out var createdAt) ? createdAt.GetString() : null;
                    data.PersonaId = dataElement.TryGetProperty("persona_id", out var personaId) ? personaId.GetString() : null;
                    data.PersonaDisplayName = dataElement.TryGetProperty("persona_display_name", out var personaDisplayName)
                        ? personaDisplayName.GetString()
                        : null;
                    data.SourceClientId = dataElement.TryGetProperty("source_client_id", out var sourceClientId) ? sourceClientId.GetString() : null;
                    data.DeliveryId = dataElement.TryGetProperty("delivery_id", out var deliveryId) ? deliveryId.GetString() : null;
                    data.Status = dataElement.TryGetProperty("status", out var status) ? status.GetString() : null;
                    data.MediaType = dataElement.TryGetProperty("media_type", out var mediaType) ? mediaType.GetString() : null;
                    data.ErrorCode = dataElement.TryGetProperty("error_code", out var errorCode) ? errorCode.GetString() : null;
                    data.ByteCount = dataElement.TryGetProperty("byte_count", out var byteCount) && byteCount.TryGetInt32(out var parsedByteCount)
                        ? parsedByteCount
                        : null;

                    if (dataElement.TryGetProperty("images", out var imagesElement) && imagesElement.ValueKind == JsonValueKind.Array)
                    {
                        var images = new List<string>();
                        foreach (var imageElement in imagesElement.EnumerateArray())
                        {
                            if (imageElement.ValueKind != JsonValueKind.String)
                            {
                                continue;
                            }

                            var dataUri = imageElement.GetString();
                            if (string.IsNullOrWhiteSpace(dataUri))
                            {
                                continue;
                            }

                            images.Add(dataUri);
                        }

                        if (images.Count > 0)
                        {
                            data.Images = images;
                        }
                    }

                    data.RequestId = dataElement.TryGetProperty("request_id", out var requestId) ? requestId.GetString() : null;
                    data.CapabilityId = dataElement.TryGetProperty("capability_id", out var capabilityId) ? capabilityId.GetString() : null;
                    data.VisionSourceId = dataElement.TryGetProperty("vision_source_id", out var visionSourceId) ? visionSourceId.GetString() : null;
                    data.SourceKind = dataElement.TryGetProperty("source_kind", out var sourceKind) ? sourceKind.GetString() : null;
                    data.SourceLabel = dataElement.TryGetProperty("source_label", out var sourceLabel) ? sourceLabel.GetString() : null;
                    data.InteractionRef = dataElement.TryGetProperty("interaction_ref", out var interactionRef)
                        ? interactionRef.GetString()
                        : null;
                    data.SpeakerRef = dataElement.TryGetProperty("speaker_ref", out var speakerRef)
                        ? speakerRef.GetString()
                        : null;
                    data.DisplayName = dataElement.TryGetProperty("display_name", out var displayName)
                        ? displayName.GetString()
                        : null;
                    data.UtteranceSeq = dataElement.TryGetProperty("utterance_seq", out var utteranceSeq) &&
                        utteranceSeq.TryGetInt32(out var utteranceSeqValue)
                            ? utteranceSeqValue
                            : null;
                    if (dataElement.TryGetProperty("participant_refs", out var participantRefsElement) &&
                        participantRefsElement.ValueKind == JsonValueKind.Array)
                    {
                        data.ParticipantRefs = participantRefsElement
                            .EnumerateArray()
                            .Where(item => item.ValueKind == JsonValueKind.String)
                            .Select(item => item.GetString())
                            .Where(item => !string.IsNullOrWhiteSpace(item))
                            .Select(item => item!)
                            .ToList();
                    }
                    if (dataElement.TryGetProperty("recipient_person_refs", out var recipientPersonRefsElement) &&
                        recipientPersonRefsElement.ValueKind == JsonValueKind.Array)
                    {
                        data.RecipientPersonRefs = recipientPersonRefsElement
                            .EnumerateArray()
                            .Where(item => item.ValueKind == JsonValueKind.String)
                            .Select(item => item.GetString())
                            .Where(item => !string.IsNullOrWhiteSpace(item))
                            .Select(item => item!)
                            .ToList();
                    }
                    data.Mode = dataElement.TryGetProperty("mode", out var mode) ? mode.GetString() : null;
                    data.TimeoutMs = dataElement.TryGetProperty("timeout_ms", out var timeoutMs) && timeoutMs.TryGetInt32(out var timeoutValue)
                        ? timeoutValue
                        : null;
                    if (string.Equals(type, "audio_runtime_state", StringComparison.Ordinal))
                    {
                        data.AudioRuntimeState =
                            JsonSerializer.Deserialize<OtomeKairoAudioRuntimeState>(dataElement.GetRawText());
                    }
                }

                ev = new OtomeKairoEvent
                {
                    EventId = eventId,
                    Type = type,
                    Data = data
                };

                return true;
            }
            catch
            {
                return false;
            }
        }

    }

    public sealed class OtomeKairoEvent
    {
        public int EventId { get; set; }
        public string Type { get; set; } = string.Empty;
        public OtomeKairoEventData Data { get; set; } = new OtomeKairoEventData();
    }

    public sealed class OtomeKairoEventData
    {
        public string? DeliveryId { get; set; }
        public string? Status { get; set; }
        public string? MediaType { get; set; }
        public string? ErrorCode { get; set; }
        public int? ByteCount { get; set; }
        public string? SystemText { get; set; }
        public string? Message { get; set; }
        public string? MessageId { get; set; }
        public string? CreatedAt { get; set; }
        public string? PersonaId { get; set; }
        public string? PersonaDisplayName { get; set; }
        public string? SourceClientId { get; set; }

        /// <summary>
        /// イベントに添付された画像（Data URI）一覧。
        /// </summary>
        public List<string>? Images { get; set; }

        public string? CapabilityId { get; set; }
        public string? RequestId { get; set; }
        public string? VisionSourceId { get; set; }
        public string? SourceKind { get; set; }
        public string? SourceLabel { get; set; }
        public string? InteractionRef { get; set; }
        public string? SpeakerRef { get; set; }
        public List<string>? ParticipantRefs { get; set; }
        public string? DisplayName { get; set; }
        public int? UtteranceSeq { get; set; }
        public List<string>? RecipientPersonRefs { get; set; }
        public string? Mode { get; set; }
        public int? TimeoutMs { get; set; }
        public OtomeKairoAudioRuntimeState? AudioRuntimeState { get; set; }
    }

    public sealed class OtomeKairoAssistantAudioReceivedEventArgs : EventArgs
    {
        public OtomeKairoAssistantAudioReceivedEventArgs(
            OtomeKairoEventData metadata,
            byte[] audioBytes)
        {
            Metadata = metadata;
            AudioBytes = audioBytes;
        }

        public OtomeKairoEventData Metadata { get; }
        public byte[] AudioBytes { get; }
    }

    public sealed class OtomeKairoCapabilityOffer
    {
        public OtomeKairoCapabilityOffer(string id, string version)
        {
            Id = id;
            Version = version;
        }

        [JsonPropertyName("id")]
        public string Id { get; }

        [JsonPropertyName("version")]
        public string Version { get; }
    }

    public sealed class OtomeKairoVisionSourceOffer
    {
        public OtomeKairoVisionSourceOffer(
            string visionSourceId,
            string capabilityId,
            string kind,
            string label,
            IReadOnlyList<string> aliases,
            IReadOnlyList<string> defaultFor,
            IReadOnlyList<string> requiredPermissions)
        {
            VisionSourceId = visionSourceId;
            CapabilityId = capabilityId;
            Kind = kind;
            Label = label;
            Aliases = aliases.ToArray();
            DefaultFor = defaultFor.ToArray();
            RequiredPermissions = requiredPermissions.ToArray();
        }

        [JsonPropertyName("vision_source_id")]
        public string VisionSourceId { get; }

        [JsonPropertyName("capability_id")]
        public string CapabilityId { get; }

        [JsonPropertyName("kind")]
        public string Kind { get; }

        [JsonPropertyName("label")]
        public string Label { get; }

        [JsonPropertyName("aliases")]
        public string[] Aliases { get; }

        [JsonPropertyName("default_for")]
        public string[] DefaultFor { get; }

        [JsonPropertyName("required_permissions")]
        public string[] RequiredPermissions { get; }
    }

    internal sealed class HelloMessage
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "hello";

        [JsonPropertyName("client_id")]
        public string ClientId { get; set; } = string.Empty;

        [JsonPropertyName("client_kind")]
        public string ClientKind { get; set; } = string.Empty;

        [JsonPropertyName("caps")]
        public OtomeKairoCapabilityOffer[] Caps { get; set; } = Array.Empty<OtomeKairoCapabilityOffer>();

        [JsonPropertyName("event_subscriptions")]
        public string[] EventSubscriptions { get; set; } = Array.Empty<string>();

        [JsonPropertyName("vision_sources")]
        public OtomeKairoVisionSourceOffer[] VisionSources { get; set; } = Array.Empty<OtomeKairoVisionSourceOffer>();
    }
}
