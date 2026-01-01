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
        private readonly Uri _webSocketUri;
        private readonly string _bearerToken;
        private ClientWebSocket? _webSocket;
        private CancellationTokenSource? _cancellationTokenSource;
        private Task? _receiveTask;
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

            await StopAsync();

            _webSocket = new ClientWebSocket();
            _cancellationTokenSource = new CancellationTokenSource();

            _webSocket.Options.SetRequestHeader("Authorization", $"Bearer {_bearerToken}");
            _webSocket.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);

            try
            {
                await _webSocket.ConnectAsync(_webSocketUri, _cancellationTokenSource.Token);
                ConnectionStateChanged?.Invoke(this, true);
                _receiveTask = Task.Run(ReceiveLoopAsync);
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, $"ログストリーム接続失敗: {ex.Message}");
                await StopAsync();
                throw;
            }
        }

        /// <summary>
        /// 接続を停止
        /// </summary>
        public async Task StopAsync()
        {
            if (_webSocket == null && _cancellationTokenSource == null && _receiveTask == null)
            {
                return;
            }

            try
            {
                _cancellationTokenSource?.Cancel();

                if (_receiveTask != null)
                {
                    try
                    {
                        await Task.WhenAny(_receiveTask, Task.Delay(TimeSpan.FromSeconds(3)));
                    }
                    catch (Exception)
                    {
                        // キャンセル時の例外は無視
                    }
                }

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
            }
            finally
            {
                _webSocket?.Dispose();
                _webSocket = null;
                _receiveTask = null;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
                ConnectionStateChanged?.Invoke(this, false);
            }
        }

        private async Task ReceiveLoopAsync()
        {
            if (_webSocket == null || _cancellationTokenSource == null)
            {
                return;
            }

            var buffer = new byte[8192];

            try
            {
                while (_webSocket.State == WebSocketState.Open &&
                       !(_cancellationTokenSource.Token.IsCancellationRequested))
                {
                    var messageBuffer = new List<byte>();
                    WebSocketReceiveResult result;

                    do
                    {
                        result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), _cancellationTokenSource.Token);

                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            return;
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
            }
            catch (OperationCanceledException)
            {
                // 停止要求なので無視
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, $"ログストリーム受信エラー: {ex.Message}");
            }
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
