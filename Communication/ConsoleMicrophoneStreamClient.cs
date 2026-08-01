using CocoroConsole.Models.OtomeKairoApi;
using CocoroConsole.Services;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using System;
using System.Diagnostics;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace CocoroConsole.Communication
{
    /// <summary>
    /// CocoroConsole端末のWASAPIマイクをOtomeKairoへ送信します。
    /// 実効入力元がこのConsoleである間だけデバイスとWebSocketを保持します。
    /// </summary>
    public sealed class ConsoleMicrophoneStreamClient : IDisposable
    {
        private const int FrameBytes = 640;
        private const int HeartbeatSeconds = 5;
        private static readonly TimeSpan InputStatePollInterval = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan ReconnectInterval = TimeSpan.FromSeconds(2);

        private readonly OtomeKairoApiClient _apiClient;
        private readonly Uri _streamUri;
        private readonly string _bearerToken;
        private readonly string _clientId;
        private readonly object _lifecycleLock = new object();
        private CancellationTokenSource? _runCancellation;
        private Task? _runTask;
        private bool _disposed;

        public ConsoleMicrophoneStreamClient(
            string baseUrl,
            string webSocketBaseUrl,
            string bearerToken,
            string clientId)
        {
            _apiClient = new OtomeKairoApiClient(baseUrl, bearerToken);
            _streamUri = new Uri($"{webSocketBaseUrl.TrimEnd('/')}/api/audio/console-stream");
            _bearerToken = bearerToken;
            _clientId = clientId;
        }

        public Task StartAsync()
        {
            lock (_lifecycleLock)
            {
                ThrowIfDisposed();
                if (_runTask != null)
                {
                    return Task.CompletedTask;
                }

                _runCancellation = new CancellationTokenSource();
                _runTask = Task.Run(() => RunAsync(_runCancellation.Token));
            }

            return Task.CompletedTask;
        }

        public async Task StopAsync()
        {
            Task? runTask;
            lock (_lifecycleLock)
            {
                _runCancellation?.Cancel();
                runTask = _runTask;
            }

            if (runTask != null)
            {
                try
                {
                    await runTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // StopAsyncによる停止を正常終了として扱います。
                }
            }

            lock (_lifecycleLock)
            {
                _runCancellation?.Dispose();
                _runCancellation = null;
                _runTask = null;
            }
        }

        private async Task RunAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var inputState = await _apiClient
                        .GetAudioInputStateAsync(cancellationToken)
                        .ConfigureAwait(false);
                    var console = inputState.Console;
                    if (!string.Equals(inputState.EffectiveSource, "console_microphone", StringComparison.Ordinal) ||
                        console == null ||
                        !string.Equals(console.ClientId, _clientId, StringComparison.Ordinal))
                    {
                        await Task.Delay(InputStatePollInterval, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    await RunCaptureSessionAsync(console, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ConsoleMicrophone] 再接続します: {ex.Message}");
                    await Task.Delay(ReconnectInterval, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        private async Task RunCaptureSessionAsync(
            OtomeKairoConsoleMicrophoneSettings settings,
            CancellationToken cancellationToken)
        {
            using var deviceEnumerator = new MMDeviceEnumerator();
            using var device = deviceEnumerator.GetDevice(settings.InputDevice.DeviceId);
            if (device.State != DeviceState.Active)
            {
                throw new InvalidOperationException("設定したConsoleマイクは現在利用できません。");
            }

            using var socket = new ClientWebSocket();
            socket.Options.SetRequestHeader("Authorization", $"Bearer {_bearerToken}");
            socket.Options.RemoteCertificateValidationCallback = (_, _, _, _) => true;
            await socket.ConnectAsync(_streamUri, cancellationToken).ConfigureAwait(false);

            using var sendLock = new SemaphoreSlim(1, 1);
            var sourceSampleRate = device.AudioClient.MixFormat.SampleRate;
            await SendJsonAsync(
                socket,
                new
                {
                    type = "audio_start",
                    protocol_version = "2",
                    client_id = _clientId,
                    input_source = "console_microphone",
                    input_session_id = (string?)null,
                    format = new
                    {
                        sample_rate = 16000,
                        channels = 1,
                        sample_format = "pcm_s16le",
                        frame_duration_ms = 20,
                        bytes_per_frame = FrameBytes,
                    },
                    device = new
                    {
                        device_id = settings.InputDevice.DeviceId,
                        name = settings.InputDevice.Name,
                    },
                    capture_settings = new
                    {
                        source_sample_rate = sourceSampleRate,
                    },
                },
                sendLock,
                cancellationToken).ConfigureAwait(false);

            var started = await ReceiveControlAsync(socket, cancellationToken).ConfigureAwait(false);
            var startedType = GetRequiredString(started, "type");
            if (!string.Equals(startedType, "audio_started", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"音声開始に失敗しました: {ReadServerMessage(started)}");
            }

            var sessionState = new CaptureSessionState
            {
                LeaseGeneration = GetRequiredInt(started, "lease_generation"),
                Paused = started.TryGetProperty("paused_reason", out var pausedReason) &&
                    pausedReason.ValueKind != JsonValueKind.Null ? 1 : 0,
            };
            var frames = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(50)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = true,
            });
            var captureFailure = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            using var capture = new WasapiCapture(device)
            {
                WaveFormat = new WaveFormat(16000, 16, 1),
            };
            var frameBuffer = new byte[FrameBytes];
            var frameOffset = 0;
            capture.DataAvailable += (_, args) =>
            {
                try
                {
                    if (Volatile.Read(ref sessionState.Paused) != 0)
                    {
                        frameOffset = 0;
                        return;
                    }

                    var sourceOffset = 0;
                    while (sourceOffset < args.BytesRecorded)
                    {
                        var copyLength = Math.Min(
                            FrameBytes - frameOffset,
                            args.BytesRecorded - sourceOffset);
                        Buffer.BlockCopy(args.Buffer, sourceOffset, frameBuffer, frameOffset, copyLength);
                        frameOffset += copyLength;
                        sourceOffset += copyLength;
                        if (frameOffset != FrameBytes)
                        {
                            continue;
                        }

                        var completedFrame = new byte[FrameBytes];
                        Buffer.BlockCopy(frameBuffer, 0, completedFrame, 0, FrameBytes);
                        if (!frames.Writer.TryWrite(completedFrame))
                        {
                            captureFailure.TrySetException(
                                new InvalidOperationException("Consoleマイクの送信キューが上限に達しました。"));
                            return;
                        }
                        frameOffset = 0;
                    }
                }
                catch (Exception ex)
                {
                    captureFailure.TrySetException(ex);
                }
            };
            capture.RecordingStopped += (_, args) =>
            {
                if (args.Exception != null)
                {
                    captureFailure.TrySetException(args.Exception);
                }
            };

            using var sessionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var sessionToken = sessionCancellation.Token;
            capture.StartRecording();
            var receiveTask = ReceiveLoopAsync(socket, sessionState, sessionToken);
            var sendTask = SendFramesAsync(socket, frames.Reader, sendLock, sessionState, sessionToken);
            var heartbeatTask = SendHeartbeatsAsync(socket, sendLock, sessionState, sessionToken);

            try
            {
                var completed = await Task.WhenAny(
                    receiveTask,
                    sendTask,
                    heartbeatTask,
                    captureFailure.Task).ConfigureAwait(false);
                await completed.ConfigureAwait(false);
            }
            finally
            {
                sessionCancellation.Cancel();
                capture.StopRecording();
                frames.Writer.TryComplete();
                await TrySendStopAsync(socket, sendLock, sessionState.LeaseGeneration).ConfigureAwait(false);
                await IgnoreSessionShutdownAsync(receiveTask, sendTask, heartbeatTask).ConfigureAwait(false);
            }
        }

        private static async Task ReceiveLoopAsync(
            ClientWebSocket socket,
            CaptureSessionState state,
            CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var control = await ReceiveControlAsync(socket, cancellationToken).ConfigureAwait(false);
                var type = GetRequiredString(control, "type");
                switch (type)
                {
                    case "audio_paused":
                        Interlocked.Exchange(ref state.Paused, 1);
                        var reason = GetRequiredString(control, "reason");
                        if (reason == "source_switched" || reason == "settings_reloaded")
                        {
                            throw new InvalidOperationException("Consoleマイクの入力リースが更新されました。");
                        }
                        break;
                    case "audio_resumed":
                        EnsureLeaseGeneration(control, state.LeaseGeneration);
                        Interlocked.Exchange(ref state.Paused, 0);
                        break;
                    case "audio_heartbeat_ack":
                        EnsureLeaseGeneration(control, state.LeaseGeneration);
                        break;
                    case "audio_stopped":
                        throw new InvalidOperationException("Consoleマイクの入力リースが終了しました。");
                    case "audio_error":
                        throw new InvalidOperationException($"Consoleマイク送信エラー: {ReadServerMessage(control)}");
                    default:
                        throw new InvalidOperationException("Consoleマイク制御メッセージのtypeが不正です。");
                }
            }
        }

        private static async Task SendFramesAsync(
            ClientWebSocket socket,
            ChannelReader<byte[]> frames,
            SemaphoreSlim sendLock,
            CaptureSessionState state,
            CancellationToken cancellationToken)
        {
            await foreach (var frame in frames.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                if (Volatile.Read(ref state.Paused) != 0)
                {
                    continue;
                }

                await sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    await socket.SendAsync(
                        new ArraySegment<byte>(frame),
                        WebSocketMessageType.Binary,
                        true,
                        cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    sendLock.Release();
                }
            }
        }

        private static async Task SendHeartbeatsAsync(
            ClientWebSocket socket,
            SemaphoreSlim sendLock,
            CaptureSessionState state,
            CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(HeartbeatSeconds), cancellationToken).ConfigureAwait(false);
                await SendJsonAsync(
                    socket,
                    new
                    {
                        type = "audio_heartbeat",
                        lease_generation = state.LeaseGeneration,
                    },
                    sendLock,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        private static async Task<JsonElement> ReceiveControlAsync(
            ClientWebSocket socket,
            CancellationToken cancellationToken)
        {
            using var message = new MemoryStream();
            var buffer = new byte[4096];
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(
                    new ArraySegment<byte>(buffer),
                    cancellationToken).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    throw new InvalidOperationException("ConsoleマイクのWebSocketが切断されました。");
                }
                if (result.MessageType != WebSocketMessageType.Text)
                {
                    throw new InvalidOperationException("Consoleマイク制御メッセージの形式が不正です。");
                }
                message.Write(buffer, 0, result.Count);
            }
            while (!result.EndOfMessage);

            using var document = JsonDocument.Parse(message.ToArray());
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException("Consoleマイク制御メッセージがJSON objectではありません。");
            }
            return document.RootElement.Clone();
        }

        private static async Task SendJsonAsync(
            ClientWebSocket socket,
            object payload,
            SemaphoreSlim sendLock,
            CancellationToken cancellationToken)
        {
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
            await sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await socket.SendAsync(
                    new ArraySegment<byte>(bytes),
                    WebSocketMessageType.Text,
                    true,
                    cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                sendLock.Release();
            }
        }

        private static async Task TrySendStopAsync(
            ClientWebSocket socket,
            SemaphoreSlim sendLock,
            int leaseGeneration)
        {
            if (socket.State != WebSocketState.Open)
            {
                return;
            }

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            try
            {
                await SendJsonAsync(
                    socket,
                    new
                    {
                        type = "audio_stop",
                        lease_generation = leaseGeneration,
                    },
                    sendLock,
                    timeout.Token).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // 切断処理ではサーバー側のdisconnect処理が同じリースを破棄します。
            }
        }

        private static async Task IgnoreSessionShutdownAsync(params Task[] tasks)
        {
            try
            {
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // 先に観測した終了理由を呼び出し元へ返します。
            }
        }

        private static string GetRequiredString(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var property) ||
                property.ValueKind != JsonValueKind.String)
            {
                throw new InvalidOperationException($"Consoleマイク制御の{propertyName}が不正です。");
            }
            return property.GetString() ?? string.Empty;
        }

        private static int GetRequiredInt(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var property) ||
                !property.TryGetInt32(out var value) ||
                value < 1)
            {
                throw new InvalidOperationException($"Consoleマイク制御の{propertyName}が不正です。");
            }
            return value;
        }

        private static void EnsureLeaseGeneration(JsonElement element, int expected)
        {
            if (GetRequiredInt(element, "lease_generation") != expected)
            {
                throw new InvalidOperationException("Consoleマイクの入力リース世代が一致しません。");
            }
        }

        private static string ReadServerMessage(JsonElement element)
        {
            if (element.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
            {
                return message.GetString() ?? "unknown";
            }
            if (element.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.String)
            {
                return code.GetString() ?? "unknown";
            }
            return "unknown";
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(ConsoleMicrophoneStreamClient));
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _runCancellation?.Cancel();
            _apiClient.Dispose();
        }

        private sealed class CaptureSessionState
        {
            public int LeaseGeneration;
            public int Paused;
        }
    }
}
