using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CocoroConsole.Communication
{
    public abstract class ResilientWebSocketClientBase : IDisposable
    {
        private static readonly TimeSpan[] DefaultReconnectDelays =
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

        protected ResilientWebSocketClientBase(Uri webSocketUri, string bearerToken)
        {
            _webSocketUri = webSocketUri ?? throw new ArgumentNullException(nameof(webSocketUri));
            _bearerToken = bearerToken ?? throw new ArgumentNullException(nameof(bearerToken));
        }

        protected ClientWebSocket? WebSocket => _webSocket;

        protected abstract string ConnectFailureLabel { get; }
        protected abstract string ReceiveFailureLabel { get; }
        protected abstract string AuthenticationFailureMessage { get; }

        public Task StartAsync()
        {
            ThrowIfDisposed();

            if (_supervisorTask != null && !_supervisorTask.IsCompleted)
            {
                return Task.CompletedTask;
            }

            _reconnectDisabled = false;
            _supervisorTokenSource = new CancellationTokenSource();
            _supervisorTask = Task.Run(() => SupervisorLoopAsync(_supervisorTokenSource.Token));
            return Task.CompletedTask;
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
                await CloseAndCleanupAsync().ConfigureAwait(false);
                _supervisorTokenSource?.Dispose();
                _supervisorTokenSource = null;
                _supervisorTask = null;
                _reconnectAttempt = 0;
                _reconnectDisabled = false;
                SetConnectionState(false);
            }
        }

        protected virtual Task OnConnectedAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        protected abstract void HandleTextMessage(string json);
        protected abstract void RaiseConnectionStateChanged(bool isConnected);
        protected abstract void RaiseError(string message);

        protected async Task SendTextAsync(string json, CancellationToken cancellationToken)
        {
            var ws = _webSocket;
            if (ws == null || ws.State != WebSocketState.Open)
            {
                return;
            }

            var bytes = Encoding.UTF8.GetBytes(json);
            await ws
                .SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken)
                .ConfigureAwait(false);
        }

        protected void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(GetType().Name);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

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

        private async Task SupervisorLoopAsync(CancellationToken supervisorToken)
        {
            try
            {
                while (!supervisorToken.IsCancellationRequested && !_reconnectDisabled)
                {
                    var connected = false;
                    try
                    {
                        await ConnectAsync(supervisorToken).ConfigureAwait(false);
                        connected = true;
                        SetConnectionState(true);
                        _reconnectAttempt = 0;

                        var closeStatus = await ReceiveLoopAsync(supervisorToken).ConfigureAwait(false);
                        if (closeStatus == WebSocketCloseStatus.PolicyViolation)
                        {
                            _reconnectDisabled = true;
                            RaiseError(AuthenticationFailureMessage);
                        }
                    }
                    catch (OperationCanceledException) when (supervisorToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        var label = connected ? ReceiveFailureLabel : ConnectFailureLabel;
                        RaiseError($"{label}: {ex.Message}");
                    }
                    finally
                    {
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
            await CloseAndCleanupAsync().ConfigureAwait(false);

            _connectionTokenSource = CancellationTokenSource.CreateLinkedTokenSource(supervisorToken);
            _webSocket = new ClientWebSocket();
            _webSocket.Options.RemoteCertificateValidationCallback = (_, _, _, _) => true;
            _webSocket.Options.SetRequestHeader("Authorization", $"Bearer {_bearerToken}");
            _webSocket.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
            await _webSocket.ConnectAsync(_webSocketUri, _connectionTokenSource.Token).ConfigureAwait(false);
            await OnConnectedAsync(_connectionTokenSource.Token).ConfigureAwait(false);
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
                    result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), token).ConfigureAwait(false);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        return result.CloseStatus;
                    }

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        messageBuffer.AddRange(buffer.AsSpan(0, result.Count).ToArray());
                    }
                } while (!result.EndOfMessage);

                if (messageBuffer.Count == 0)
                {
                    continue;
                }

                var json = Encoding.UTF8.GetString(messageBuffer.ToArray());
                HandleTextMessage(json);
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
            var index = Math.Min(attempt, DefaultReconnectDelays.Length - 1);
            var jitterMs = Random.Shared.Next(0, 250);
            return DefaultReconnectDelays[index] + TimeSpan.FromMilliseconds(jitterMs);
        }

        private void SetConnectionState(bool isConnected)
        {
            if (_isConnected == isConnected)
            {
                return;
            }

            _isConnected = isConnected;
            RaiseConnectionStateChanged(isConnected);
        }
    }
}
