using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace CocoroConsole.Communication
{
    /// <summary>
    /// cocoro_ghost の /api/events/stream に接続してイベント(notification/meta-request/desktop_watch + vision command)を受信するクライアント
    /// </summary>
    public sealed class EventsStreamClient : IDisposable
    {
        private static readonly TimeSpan[] ReconnectDelays =
        {
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(60),
        };

        private readonly Uri _webSocketUri;
        private readonly string _bearerToken;
        private readonly string? _clientId;
        private readonly IReadOnlyList<string>? _caps;
        private ClientWebSocket? _webSocket;
        private CancellationTokenSource? _connectionTokenSource;
        private CancellationTokenSource? _supervisorTokenSource;
        private Task? _supervisorTask;
        private bool _isConnected;
        private int _reconnectAttempt;
        private bool _reconnectDisabled;
        private bool _disposed;

        public event EventHandler<CocoroGhostEvent>? EventReceived;
        public event EventHandler<bool>? ConnectionStateChanged;
        public event EventHandler<string>? ErrorOccurred;

        public EventsStreamClient(Uri webSocketUri, string bearerToken, string? clientId = null, IReadOnlyList<string>? caps = null)
        {
            _webSocketUri = webSocketUri ?? throw new ArgumentNullException(nameof(webSocketUri));
            _bearerToken = bearerToken ?? throw new ArgumentNullException(nameof(bearerToken));
            _clientId = string.IsNullOrWhiteSpace(clientId) ? null : clientId.Trim();
            _caps = caps;
        }

        public async Task StartAsync()
        {
            ThrowIfDisposed();

            if (_supervisorTask != null && !_supervisorTask.IsCompleted)
            {
                return;
            }

            _reconnectDisabled = false;
            _supervisorTokenSource = new CancellationTokenSource();
            _supervisorTask = Task.Run(() => SupervisorLoopAsync(_supervisorTokenSource.Token));
            await Task.CompletedTask;
        }

        public async Task StopAsync()
        {
            if (_supervisorTask == null && _connectionTokenSource == null && _webSocket == null)
            {
                return;
            }

            try
            {
                _supervisorTokenSource?.Cancel();

                if (_supervisorTask != null)
                {
                    try
                    {
                        // UIスレッドの同期呼び出し（Disposeなど）でもデッドロックしないようにコンテキストを捕捉しない
                        await Task
                            .WhenAny(_supervisorTask, Task.Delay(TimeSpan.FromSeconds(3)))
                            .ConfigureAwait(false);
                    }
                    catch
                    {
                        // キャンセル時の例外は無視
                    }
                }
            }
            finally
            {
                // UIスレッドの同期呼び出し（Disposeなど）でもデッドロックしないようにコンテキストを捕捉しない
                await CloseAndCleanupAsync().ConfigureAwait(false);
                _supervisorTokenSource?.Dispose();
                _supervisorTokenSource = null;
                _supervisorTask = null;
                _reconnectAttempt = 0;
                _reconnectDisabled = false;
                SetConnectionState(false);
            }
        }

        private async Task SupervisorLoopAsync(CancellationToken supervisorToken)
        {
            try
            {
                while (!supervisorToken.IsCancellationRequested && !_reconnectDisabled)
                {
                    var connected = false;
                    try
                    {
                        // ライブラリ側ではUIコンテキストを捕捉しない
                        await ConnectAsync(supervisorToken).ConfigureAwait(false);
                        connected = true;
                        SetConnectionState(true);
                        _reconnectAttempt = 0;

                        // ライブラリ側ではUIコンテキストを捕捉しない
                        var closeStatus = await ReceiveLoopAsync(supervisorToken).ConfigureAwait(false);
                        if (closeStatus == WebSocketCloseStatus.PolicyViolation)
                        {
                            _reconnectDisabled = true;
                            ErrorOccurred?.Invoke(this, "イベントストリーム認証エラーのため再接続を停止しました。");
                        }
                    }
                    catch (OperationCanceledException) when (supervisorToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        var label = connected ? "イベントストリーム受信エラー" : "イベントストリーム接続失敗";
                        ErrorOccurred?.Invoke(this, $"{label}: {ex.Message}");
                    }
                    finally
                    {
                        // ライブラリ側ではUIコンテキストを捕捉しない
                        await CloseAndCleanupAsync().ConfigureAwait(false);
                        if (connected)
                        {
                            SetConnectionState(false);
                        }
                    }

                    if (supervisorToken.IsCancellationRequested || _reconnectDisabled)
                    {
                        break;
                    }

                    var delay = GetReconnectDelay(_reconnectAttempt++);
                    // ライブラリ側ではUIコンテキストを捕捉しない
                    await Task.Delay(delay, supervisorToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // 停止要求なので無視
            }
        }

        private async Task ConnectAsync(CancellationToken supervisorToken)
        {
            // ライブラリ側ではUIコンテキストを捕捉しない
            await CloseAndCleanupAsync().ConfigureAwait(false);

            _connectionTokenSource = CancellationTokenSource.CreateLinkedTokenSource(supervisorToken);
            _webSocket = new ClientWebSocket();
            _webSocket.Options.SetRequestHeader("Authorization", $"Bearer {_bearerToken}");
            _webSocket.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
            // ライブラリ側ではUIコンテキストを捕捉しない
            await _webSocket.ConnectAsync(_webSocketUri, _connectionTokenSource.Token).ConfigureAwait(false);

            // ライブラリ側ではUIコンテキストを捕捉しない
            await SendHelloIfNeededAsync(_connectionTokenSource.Token).ConfigureAwait(false);
        }

        private async Task SendHelloIfNeededAsync(CancellationToken cancellationToken)
        {
            var ws = _webSocket;
            if (ws == null || ws.State != WebSocketState.Open)
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
                    Caps = _caps?.ToArray() ?? new[] { "vision.desktop", "vision.camera" }
                };

                var json = JsonSerializer.Serialize(payload);
                var bytes = Encoding.UTF8.GetBytes(json);
                // ライブラリ側ではUIコンテキストを捕捉しない
                await ws
                    .SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[EventsStream] hello send error: {ex.Message}");
            }
        }

        private async Task<WebSocketCloseStatus?> ReceiveLoopAsync(CancellationToken supervisorToken)
        {
            var ws = _webSocket;
            if (ws == null)
            {
                return null;
            }

            var token = _connectionTokenSource?.Token ?? supervisorToken;
            var buffer = new byte[8192];

            while (ws.State == WebSocketState.Open && !token.IsCancellationRequested)
            {
                var messageBuffer = new List<byte>();
                WebSocketReceiveResult result;

                do
                {
                    // ライブラリ側ではUIコンテキストを捕捉しない
                    result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), token).ConfigureAwait(false);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        return result.CloseStatus;
                    }

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        messageBuffer.AddRange(buffer.Take(result.Count));
                    }
                } while (!result.EndOfMessage);

                if (messageBuffer.Count == 0)
                {
                    continue;
                }

                var json = Encoding.UTF8.GetString(messageBuffer.ToArray());
                HandleMessage(json);
            }

            return null;
        }

        private async Task CloseAndCleanupAsync()
        {
            _connectionTokenSource?.Cancel();

            if (_webSocket?.State == WebSocketState.Open || _webSocket?.State == WebSocketState.CloseReceived)
            {
                try
                {
                    // --- Close ハンドシェイクが無期限に詰まるケース対策 ---
                    // サーバーが落ちた直後などで CloseAsync が戻らないと、終了処理で UI スレッドが固まる。
                    // ライブラリ側ではUIコンテキストを捕捉しない
                    await _webSocket
                        .CloseAsync(WebSocketCloseStatus.NormalClosure, "停止", CancellationToken.None)
                        .WaitAsync(TimeSpan.FromSeconds(1))
                        .ConfigureAwait(false);
                }
                catch
                {
                    // Close失敗は無視
                }
            }

            _webSocket?.Dispose();
            _webSocket = null;

            _connectionTokenSource?.Dispose();
            _connectionTokenSource = null;
        }

        private static TimeSpan GetReconnectDelay(int attempt)
        {
            var index = Math.Min(attempt, ReconnectDelays.Length - 1);
            var jitterMs = Random.Shared.Next(0, 250);
            return ReconnectDelays[index] + TimeSpan.FromMilliseconds(jitterMs);
        }

        private void SetConnectionState(bool isConnected)
        {
            if (_isConnected == isConnected)
            {
                return;
            }

            _isConnected = isConnected;
            ConnectionStateChanged?.Invoke(this, isConnected);
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

        private static bool TryParseEvent(JsonElement element, out CocoroGhostEvent ev)
        {
            ev = new CocoroGhostEvent();

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

                // --- event_id（cocoro_ghost の events.event_id、命令は 0） ---
                if (!element.TryGetProperty("event_id", out var eventIdElement))
                {
                    return false;
                }
                if (!eventIdElement.TryGetInt32(out var eventId))
                {
                    return false;
                }

                var data = new CocoroGhostEventData();
                if (element.TryGetProperty("data", out var dataElement) && dataElement.ValueKind == JsonValueKind.Object)
                {
                    // --- 通常イベント（notification/meta-request/desktop_watch/reminder） ---
                    data.SystemText = dataElement.TryGetProperty("system_text", out var systemText) ? systemText.GetString() : null;
                    data.Message = dataElement.TryGetProperty("message", out var message) ? message.GetString() : null;

                    // --- 添付画像（notification等） ---
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
                    data.Source = dataElement.TryGetProperty("source", out var source) ? source.GetString() : null;
                    data.Mode = dataElement.TryGetProperty("mode", out var mode) ? mode.GetString() : null;
                    data.Purpose = dataElement.TryGetProperty("purpose", out var purpose) ? purpose.GetString() : null;
                    data.TimeoutMs = dataElement.TryGetProperty("timeout_ms", out var timeoutMs) && timeoutMs.TryGetInt32(out var timeoutValue)
                        ? timeoutValue
                        : null;
                }

                ev = new CocoroGhostEvent
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

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(EventsStreamClient));
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                StopAsync().GetAwaiter().GetResult();
            }
            catch
            {
                // Disposeでは握りつぶす
            }
        }
    }

    public sealed class CocoroGhostEvent
    {
        public int EventId { get; set; }
        public string Type { get; set; } = string.Empty;
        public CocoroGhostEventData Data { get; set; } = new CocoroGhostEventData();
    }

    public sealed class CocoroGhostEventData
    {
        public string? SystemText { get; set; }
        public string? Message { get; set; }

        /// <summary>
        /// 通知などで添付された画像（Data URI）一覧。
        /// </summary>
        public List<string>? Images { get; set; }

        // Vision command
        public string? RequestId { get; set; }
        public string? Source { get; set; }
        public string? Mode { get; set; }
        public string? Purpose { get; set; }
        public int? TimeoutMs { get; set; }
    }

    internal sealed class HelloMessage
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "hello";

        [JsonPropertyName("client_id")]
        public string ClientId { get; set; } = string.Empty;

        [JsonPropertyName("caps")]
        public string[] Caps { get; set; } = Array.Empty<string>();
    }
}
