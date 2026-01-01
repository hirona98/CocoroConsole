using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CocoroConsole.Communication
{
    /// <summary>
    /// cocoro_ghost の /api/events/stream に接続してイベント(notification/meta-request)を受信するクライアント
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

        public EventsStreamClient(Uri webSocketUri, string bearerToken)
        {
            _webSocketUri = webSocketUri ?? throw new ArgumentNullException(nameof(webSocketUri));
            _bearerToken = bearerToken ?? throw new ArgumentNullException(nameof(bearerToken));
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
                        await Task.WhenAny(_supervisorTask, Task.Delay(TimeSpan.FromSeconds(3)));
                    }
                    catch
                    {
                        // キャンセル時の例外は無視
                    }
                }
            }
            finally
            {
                await CloseAndCleanupAsync();
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
                        await ConnectAsync(supervisorToken);
                        connected = true;
                        SetConnectionState(true);
                        _reconnectAttempt = 0;

                        var closeStatus = await ReceiveLoopAsync(supervisorToken);
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
                        await CloseAndCleanupAsync();
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
                    await Task.Delay(delay, supervisorToken);
                }
            }
            catch (OperationCanceledException)
            {
                // 停止要求なので無視
            }
        }

        private async Task ConnectAsync(CancellationToken supervisorToken)
        {
            await CloseAndCleanupAsync();

            _connectionTokenSource = CancellationTokenSource.CreateLinkedTokenSource(supervisorToken);
            _webSocket = new ClientWebSocket();
            _webSocket.Options.SetRequestHeader("Authorization", $"Bearer {_bearerToken}");
            _webSocket.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
            await _webSocket.ConnectAsync(_webSocketUri, _connectionTokenSource.Token);
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
                    result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), token);

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
                    await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "停止", CancellationToken.None);
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

                DateTime timestamp = DateTime.UtcNow;
                if (element.TryGetProperty("ts", out var tsElement))
                {
                    var tsString = tsElement.GetString();
                    if (!string.IsNullOrEmpty(tsString) &&
                        DateTime.TryParse(tsString, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed))
                    {
                        timestamp = parsed;
                    }
                }

                var data = new CocoroGhostEventData();
                if (element.TryGetProperty("data", out var dataElement) && dataElement.ValueKind == JsonValueKind.Object)
                {
                    // New (2025-12) events payload: {data:{system_text,message}}
                    data.SystemText = dataElement.TryGetProperty("system_text", out var systemText) ? systemText.GetString() : null;
                    data.Message = dataElement.TryGetProperty("message", out var message) ? message.GetString() : null;

                    // Legacy fields (keep backward compatibility)
                    data.SourceSystem = dataElement.TryGetProperty("source_system", out var sourceSystem) ? sourceSystem.GetString() : null;
                    data.Title = dataElement.TryGetProperty("title", out var title) ? title.GetString() : null;
                    data.Body = dataElement.TryGetProperty("body", out var body) ? body.GetString() : null;
                    data.ResultText = dataElement.TryGetProperty("result_text", out var resultText) ? resultText.GetString() : null;
                }

                ev = new CocoroGhostEvent
                {
                    EventId = element.TryGetProperty("event_id", out var eventId) ? eventId.GetString() : null,
                    Timestamp = timestamp.ToLocalTime(),
                    Type = type,
                    MemoryId = element.TryGetProperty("memory_id", out var memoryId) ? memoryId.GetString() : null,
                    UnitId = element.TryGetProperty("unit_id", out var unitId) && unitId.TryGetInt32(out var unitIdValue) ? unitIdValue : null,
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
        public string? EventId { get; set; }
        public DateTime Timestamp { get; set; }
        public string Type { get; set; } = string.Empty;
        public string? MemoryId { get; set; }
        public int? UnitId { get; set; }
        public CocoroGhostEventData Data { get; set; } = new CocoroGhostEventData();
    }

    public sealed class CocoroGhostEventData
    {
        public string? SystemText { get; set; }
        public string? Message { get; set; }
        public string? SourceSystem { get; set; }
        public string? Title { get; set; }
        public string? Body { get; set; }
        public string? ResultText { get; set; }
    }
}
