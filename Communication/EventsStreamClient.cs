using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace CocoroConsole.Communication
{
    /// <summary>
    /// OtomeKairo の /api/events/stream に接続してサーバー駆動のイベントと制御要求を受信するクライアント。
    /// </summary>
    public sealed class EventsStreamClient : ResilientWebSocketClientBase
    {
        private readonly string? _clientId;
        private readonly IReadOnlyList<OtomeKairoCapabilityOffer>? _caps;

        public event EventHandler<OtomeKairoEvent>? EventReceived;
        public event EventHandler<bool>? ConnectionStateChanged;
        public event EventHandler<string>? ErrorOccurred;

        public EventsStreamClient(Uri webSocketUri, string bearerToken, string? clientId = null, IReadOnlyList<OtomeKairoCapabilityOffer>? caps = null)
            : base(webSocketUri, bearerToken)
        {
            _clientId = string.IsNullOrWhiteSpace(clientId) ? null : clientId.Trim();
            _caps = caps;
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
                    Caps = _caps?.ToArray() ?? new[] { new OtomeKairoCapabilityOffer("vision.capture", "1") }
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
                            EventReceived?.Invoke(this, ev);
                        }
                    }
                    return;
                }

                if (TryParseEvent(root, out var singleEvent))
                {
                    EventReceived?.Invoke(this, singleEvent);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[EventsStream] JSON parse error: {ex.Message}");
                ErrorOccurred?.Invoke(this, $"イベントパースエラー: {ex.Message}");
            }
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
                    // --- desktop_watch ---
                    data.SystemText = dataElement.TryGetProperty("system_text", out var systemText) ? systemText.GetString() : null;
                    data.Message = dataElement.TryGetProperty("message", out var message) ? message.GetString() : null;

                    // --- desktop_watch の添付画像 ---
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

                    // --- vision.capture_request ---
                    data.RequestId = dataElement.TryGetProperty("request_id", out var requestId) ? requestId.GetString() : null;
                    data.CapabilityId = dataElement.TryGetProperty("capability_id", out var capabilityId) ? capabilityId.GetString() : null;
                    data.Source = dataElement.TryGetProperty("source", out var source) ? source.GetString() : null;
                    data.Mode = dataElement.TryGetProperty("mode", out var mode) ? mode.GetString() : null;
                    data.TimeoutMs = dataElement.TryGetProperty("timeout_ms", out var timeoutMs) && timeoutMs.TryGetInt32(out var timeoutValue)
                        ? timeoutValue
                        : null;
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
        public string? SystemText { get; set; }
        public string? Message { get; set; }

        /// <summary>
        /// イベントに添付された画像（Data URI）一覧。
        /// </summary>
        public List<string>? Images { get; set; }

        // vision.capture_request のデータ
        public string? RequestId { get; set; }
        public string? CapabilityId { get; set; }
        public string? Source { get; set; }
        public string? Mode { get; set; }
        public int? TimeoutMs { get; set; }
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

    internal sealed class HelloMessage
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "hello";

        [JsonPropertyName("client_id")]
        public string ClientId { get; set; } = string.Empty;

        [JsonPropertyName("caps")]
        public OtomeKairoCapabilityOffer[] Caps { get; set; } = Array.Empty<OtomeKairoCapabilityOffer>();
    }
}
