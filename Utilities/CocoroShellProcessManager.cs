using CocoroConsole.Services;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CocoroConsole.Utilities
{
    /// <summary>
    /// Consoleが起動したShellのプロセスと稼働状態を管理する。
    /// 操作はMainWindowのセマフォで直列化し、UIスレッドから呼び出す。
    /// </summary>
    public sealed class CocoroShellProcessManager : IDisposable
    {
        private readonly IAppSettings _appSettings;
        private Process? _process;
        private HttpClient? _client;
        private int _runningPort;
        private string? _appliedSettings;

        public event EventHandler? StateChanged;
        public bool IsRunning => _process != null && !_process.HasExited;
        public bool IsBusy { get; private set; }
        public string? Error { get; private set; }
        public string Operation { get; private set; } = string.Empty;

        public CocoroShellProcessManager(IAppSettings appSettings)
        {
            _appSettings = appSettings;
        }

        public static void PrepareSessionToken(IAppSettings appSettings, bool rotate)
        {
            if (rotate || string.IsNullOrEmpty(appSettings.ShellSessionToken))
            {
                appSettings.ShellSessionToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            }
        }

        /// <summary>
        /// 初回接続では保存済みON/OFFを復元し、以降は稼働中の設定変更だけを反映する。
        /// </summary>
        public async Task ApplyAsync(bool initialConnection, CancellationToken cancellationToken)
        {
            if (!_appSettings.ConsoleClient.Process.CocoroShellEnabled || !HasDisplayableAvatar())
            {
                await StopAsync(cancellationToken);
                return;
            }

            var settings = JsonSerializer.Serialize(_appSettings.BuildShellRuntimeConfig());
            if (IsRunning && settings != _appliedSettings)
            {
                await StopAsync(cancellationToken);
                await StartAsync(cancellationToken);
            }
            else if (initialConnection && !IsRunning)
            {
                await StartAsync(cancellationToken);
            }
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            RefreshState();
            if (IsRunning)
            {
                return;
            }

            BeginOperation("起動中");
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!HasDisplayableAvatar())
                {
                    throw new InvalidOperationException("表示するアバターが設定されていません。");
                }

                var executable = Path.Combine(AppContext.BaseDirectory, "CocoroShell", "CocoroShell.exe");
                if (!File.Exists(executable))
                {
                    throw new FileNotFoundException("CocoroShell.exeが見つかりません。", executable);
                }
                if (IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners()
                    .Any(endpoint => endpoint.Port == _appSettings.CocoroShellPort))
                {
                    throw new InvalidOperationException($"CocoroShellのポート {_appSettings.CocoroShellPort} は使用中です。Unity Editorなどの使用元を停止してください。");
                }

                PrepareSessionToken(_appSettings, true);
                _runningPort = _appSettings.CocoroShellPort;
                _client?.Dispose();
                _client = new HttpClient
                {
                    BaseAddress = new Uri($"http://127.0.0.1:{_runningPort}"),
                    Timeout = TimeSpan.FromSeconds(5)
                };
                _client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", _appSettings.ShellSessionToken);

                var startInfo = new ProcessStartInfo(executable)
                {
                    UseShellExecute = false,
                    WorkingDirectory = Path.GetDirectoryName(executable),
                };
                startInfo.Environment["COCORO_CONSOLE_API_URL"] = $"http://127.0.0.1:{_appSettings.CocoroConsolePort}";
                startInfo.Environment["COCORO_SHELL_SESSION_TOKEN"] = _appSettings.ShellSessionToken;
                cancellationToken.ThrowIfCancellationRequested();
                _process = Process.Start(startInfo)
                    ?? throw new InvalidOperationException("CocoroShellのプロセスを起動できませんでした。");
                _appliedSettings = JsonSerializer.Serialize(_appSettings.BuildShellRuntimeConfig());
                StateChanged?.Invoke(this, EventArgs.Empty);

                using var startupTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                startupTimeout.CancelAfter(TimeSpan.FromSeconds(60));
                try
                {
                    while (true)
                    {
                        startupTimeout.Token.ThrowIfCancellationRequested();
                        if (!IsRunning)
                        {
                            throw new InvalidOperationException("CocoroShellが起動中に終了しました。");
                        }
                        try
                        {
                            using var response = await _client.GetAsync("/api/status", startupTimeout.Token);
                            response.EnsureSuccessStatusCode();
                            using var stream = await response.Content.ReadAsStreamAsync(startupTimeout.Token);
                            using var status = await JsonDocument.ParseAsync(stream, cancellationToken: startupTimeout.Token);
                            var owner = await Task.Run(() => ProcessHelper.GetProcessIdByPort(_runningPort), startupTimeout.Token);
                            if (owner != _process.Id)
                            {
                                throw new InvalidOperationException("CocoroShellの待受ポートを別のプロセスが使用しています。");
                            }
                            var avatarState = status.RootElement.GetProperty("avatarState").GetString();
                            if (avatarState == "error")
                            {
                                throw new InvalidOperationException($"アバターの読み込みに失敗しました: {status.RootElement.GetProperty("avatarLoadError").GetString()}");
                            }
                            if (avatarState == "ready")
                            {
                                return;
                            }
                        }
                        catch (HttpRequestException ex) when (ex.StatusCode == null && !startupTimeout.IsCancellationRequested)
                        {
                            // プロセス起動からAPI待受開始まで待機する。
                        }
                        await Task.Delay(500, startupTimeout.Token);
                    }
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new TimeoutException("CocoroShellの起動確認がタイムアウトしました。");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Error = ex.Message;
                throw;
            }
            finally
            {
                EndOperation();
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            RefreshState();
            if (!IsRunning)
            {
                return;
            }

            BeginOperation("終了中");
            try
            {
                var process = _process!;
                // 起動確認の取り消し直後でも、対象自身のAPI待受が始まるまで待つ。
                var waiting = Stopwatch.StartNew();
                int? owner;
                do
                {
                    owner = await Task.Run(() => ProcessHelper.GetProcessIdByPort(_runningPort), cancellationToken);
                    if (owner.HasValue || process.HasExited || waiting.Elapsed >= TimeSpan.FromSeconds(10))
                    {
                        break;
                    }
                    await Task.Delay(200, cancellationToken);
                } while (true);
                if (process.HasExited)
                {
                    return;
                }
                if (owner != process.Id)
                {
                    throw new InvalidOperationException("起動したCocoroShellのAPIを確認できないため、終了要求を送信できません。");
                }
                using var response = await _client!.PostAsJsonAsync(
                    "/api/control", new { action = "shutdown" }, cancellationToken);
                response.EnsureSuccessStatusCode();
                await process.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
                ReleaseProcess();
                _appSettings.ShellSessionToken = string.Empty;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Error = ex.Message;
                throw;
            }
            finally
            {
                EndOperation();
            }
        }

        public void RefreshState()
        {
            if (!IsBusy && _process != null && _process.HasExited)
            {
                ReleaseProcess();
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private bool HasDisplayableAvatar()
        {
            var presentation = _appSettings.GetCurrentAvatarPresentation();
            return presentation != null && !string.IsNullOrWhiteSpace(presentation.Model);
        }

        private void BeginOperation(string operation)
        {
            IsBusy = true;
            Error = null;
            Operation = operation;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        private void EndOperation()
        {
            IsBusy = false;
            Operation = string.Empty;
            RefreshState();
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        private void ReleaseProcess()
        {
            _process?.Dispose();
            _process = null;
            _client?.Dispose();
            _client = null;
            _appliedSettings = null;
        }

        public void Dispose()
        {
            ReleaseProcess();
        }
    }
}
