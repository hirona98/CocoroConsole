using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CocoroConsole.Communication;

namespace CocoroConsole.Communication
{
    /// <summary>
    /// cocoro_ghost の /api/logs/stream に接続してログを受信するクライアント
    /// </summary>
    public class LogStreamClient : IDisposable
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

        public event EventHandler<IReadOnlyList<LogMessage>>? LogsReceived;
        public event EventHandler<bool>? ConnectionStateChanged;
        public event EventHandler<string>? ErrorOccurred;

        public LogStreamClient(Uri webSocketUri, string bearerToken)
        {
            _webSocketUri = webSocketUri ?? throw new ArgumentNullException(nameof(webSocketUri));
            _bearerToken = bearerToken ?? throw new ArgumentNullException(nameof(bearerToken));
        }

        /// <summary>
        /// 接続を開始
        /// </summary>
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

        /// <summary>
        /// 接続を停止
        /// </summary>
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
                    catch (Exception)
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
                            ErrorOccurred?.Invoke(this, "ログストリーム認証エラーのため再接続を停止しました。");
                        }
                    }
                    catch (OperationCanceledException) when (supervisorToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        var label = connected ? "ログストリーム受信エラー" : "ログストリーム接続失敗";
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
                catch (Exception)
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
                var logs = new List<LogMessage>();

                if (root.ValueKind == JsonValueKind.Array)
                {
                    foreach (var element in root.EnumerateArray())
                    {
                        if (TryParseLog(element, out var log))
                        {
                            logs.Add(log);
                        }
                    }
                }
                else if (root.ValueKind == JsonValueKind.Object && TryParseLog(root, out var logMessage))
                {
                    logs.Add(logMessage);
                }

                if (logs.Count > 0)
                {
                    LogsReceived?.Invoke(this, logs);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LogStream] JSON parse error: {ex.Message}");
                ErrorOccurred?.Invoke(this, $"ログパースエラー: {ex.Message}");
            }
        }

        private bool TryParseLog(JsonElement element, out LogMessage logMessage)
        {
            logMessage = new LogMessage();
            try
            {
                DateTime timestamp = DateTime.Now;
                if (element.TryGetProperty("ts", out var tsElement))
                {
                    var tsString = tsElement.GetString();
                    if (!string.IsNullOrEmpty(tsString) &&
                        DateTime.TryParse(tsString, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed))
                    {
                        timestamp = parsed.ToLocalTime();
                    }
                }

                var level = element.TryGetProperty("level", out var levelElement) ? levelElement.GetString() : "INFO";
                var logger = element.TryGetProperty("logger", out var loggerElement) ? loggerElement.GetString() : string.Empty;
                var message = element.TryGetProperty("msg", out var msgElement) ? msgElement.GetString() : string.Empty;
                message = CompactJsonWhitespaceInLog(message ?? string.Empty);

                logMessage = new LogMessage
                {
                    timestamp = timestamp,
                    level = level ?? "INFO",
                    component = logger ?? string.Empty,
                    message = message ?? string.Empty
                };
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string CompactJsonWhitespaceInLog(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return message;
            }

            var markerIndex = FindFirstMarkerIndex(message);
            if (markerIndex < 0)
            {
                return message;
            }

            var jsonStart = message.IndexOfAny(new[] { '{', '[' }, markerIndex);
            if (jsonStart < 0)
            {
                return message;
            }

            var prefix = message.Substring(0, jsonStart);
            var jsonPart = message.Substring(jsonStart);
            var compacted = CollapseWhitespaceOutsideStrings(jsonPart);
            return prefix + compacted;
        }

        private static int FindFirstMarkerIndex(string message)
        {
            string[] markers =
            {
                "LLM response (json)",
                "LLM request (json)",
                "LLM response (chat)",
                "LLM request (chat)",
                "LLM response (vision)",
                "LLM request (vision)",
            };

            var bestIndex = -1;
            foreach (var marker in markers)
            {
                var index = message.IndexOf(marker, StringComparison.Ordinal);
                if (index < 0)
                {
                    continue;
                }

                if (bestIndex < 0 || index < bestIndex)
                {
                    bestIndex = index;
                }
            }

            return bestIndex;
        }

        private static string CollapseWhitespaceOutsideStrings(string text)
        {
            var sb = new StringBuilder(text.Length);
            bool inString = false;
            bool escaped = false;
            bool inWhitespace = false;

            foreach (var ch in text)
            {
                if (inString)
                {
                    sb.Append(ch);
                    if (escaped)
                    {
                        escaped = false;
                    }
                    else if (ch == '\\')
                    {
                        escaped = true;
                    }
                    else if (ch == '"')
                    {
                        inString = false;
                    }
                    continue;
                }

                if (ch == '"')
                {
                    inString = true;
                    inWhitespace = false;
                    sb.Append(ch);
                    continue;
                }

                if (char.IsWhiteSpace(ch))
                {
                    if (!inWhitespace)
                    {
                        sb.Append(' ');
                        inWhitespace = true;
                    }
                    continue;
                }

                inWhitespace = false;
                sb.Append(ch);
            }

            return sb.ToString();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(LogStreamClient));
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
            catch (Exception)
            {
                // Disposeでは握りつぶす
            }
        }
    }
}
