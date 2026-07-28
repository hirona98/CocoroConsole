using CocoroConsole.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CocoroConsole.Communication
{
    /// <summary>
    /// CocoroConsole用REST APIサーバー
    /// </summary>
    public class CocoroConsoleApiServer : IDisposable
    {
        private const string ShellEditorBootstrapPipeName = "CocoroAI.CocoroShell.EditorBootstrap.v1";
        private const string ShellEditorBootstrapRequest = "cocoroshell-editor-bootstrap-v1";

        private IHost? _host;
        private readonly int _port;
        private readonly IAppSettings _appSettings;
        private readonly Func<CancellationToken, Task> _saveConsoleClientSettingsAsync;
        private CancellationTokenSource? _cts;
        private Task? _shellEditorBootstrapTask;

        // イベント
        public event EventHandler<UiMessageRequest>? UiMessageReceived;
        public event EventHandler<ControlRequest>? ControlCommandReceived;
        public event EventHandler<StatusUpdateRequest>? StatusUpdateReceived;

        public bool IsRunning => _host != null;

        public CocoroConsoleApiServer(
            int port,
            IAppSettings appSettings,
            Func<CancellationToken, Task> saveConsoleClientSettingsAsync)
        {
            _port = port;
            _appSettings = appSettings;
            _saveConsoleClientSettingsAsync = saveConsoleClientSettingsAsync;
        }

        /// <summary>
        /// APIサーバーを開始
        /// </summary>
        public async Task StartAsync()
        {
            if (_host != null)
            {
                return;
            }

            try
            {
                _cts = new CancellationTokenSource();

                var builder = WebApplication.CreateBuilder();
                builder.WebHost.UseUrls($"http://127.0.0.1:{_port}");

                // ログレベルを設定してHTTPリクエストログを無効化
                builder.Logging.ClearProviders();
                builder.Logging.SetMinimumLevel(LogLevel.Warning);

                // Kestrelサーバーの設定
                builder.WebHost.ConfigureKestrel(serverOptions =>
                {
                    serverOptions.ListenLocalhost(_port);
                });

                // サービスの登録
                builder.Services.AddSingleton(_appSettings);
                builder.Services.AddSingleton(this);

                var app = builder.Build();

                // グローバル例外ハンドラー
                app.UseExceptionHandler(appError =>
                {
                    appError.Run(async context =>
                    {
                        context.Response.StatusCode = 500;
                        context.Response.ContentType = "application/json; charset=utf-8";

                        var contextFeature = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();
                        if (contextFeature != null)
                        {
                            Debug.WriteLine($"APIサーバー例外: {contextFeature.Error}");

                            var errorResponse = new ErrorResponse
                            {
                                message = "Internal server error",
                                errorCode = "INTERNAL_ERROR"
                            };

                            await context.Response.WriteAsJsonAsync(errorResponse);
                        }
                    });
                });

                // エンドポイントの設定
                ConfigureEndpoints(app);

                _host = app;
                await _host.StartAsync(_cts.Token).ConfigureAwait(false);
                _shellEditorBootstrapTask = RunShellEditorBootstrapPipeAsync(_cts.Token);

                Debug.WriteLine($"CocoroConsole APIサーバーを起動しました: http://127.0.0.1:{_port}");
            }
            catch (Exception ex)
            {
                _cts?.Cancel();
                _host?.Dispose();
                _host = null;
                _shellEditorBootstrapTask = null;
                _cts?.Dispose();
                _cts = null;
                Debug.WriteLine($"APIサーバー起動エラー: {ex.Message}");
                throw new InvalidOperationException($"APIサーバーの起動に失敗しました: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// エンドポイントを設定
        /// </summary>
        private void ConfigureEndpoints(WebApplication app)
        {
            // POST /api/ui/messages - UIメッセージ受信
            app.MapPost("/api/ui/messages", async (HttpContext context) =>
            {
                try
                {
                    var request = await context.Request.ReadFromJsonAsync<UiMessageRequest>();
                    if (request == null)
                    {
                        context.Response.StatusCode = 400;
                        await context.Response.WriteAsJsonAsync(new ErrorResponse
                        {
                            message = "Request body is required",
                            errorCode = "INVALID_REQUEST"
                        });
                        return;
                    }

                    // 検証
                    if (string.IsNullOrWhiteSpace(request.content))
                    {
                        context.Response.StatusCode = 400;
                        await context.Response.WriteAsJsonAsync(new ErrorResponse
                        {
                            message = "Field 'content' is required and cannot be empty",
                            errorCode = "VALIDATION_ERROR"
                        });
                        return;
                    }

                    if (request.role != "user" && request.role != "assistant")
                    {
                        context.Response.StatusCode = 400;
                        await context.Response.WriteAsJsonAsync(new ErrorResponse
                        {
                            message = "Field 'role' must be 'user' or 'assistant'",
                            errorCode = "VALIDATION_ERROR"
                        });
                        return;
                    }

                    // イベント発火
                    UiMessageReceived?.Invoke(this, request);

                    // 成功レスポンス
                    await context.Response.WriteAsJsonAsync(new StandardResponse
                    {
                        status = "success",
                        message = "UI message received"
                    });
                }
                catch (System.Text.Json.JsonException)
                {
                    context.Response.StatusCode = 400;
                    await context.Response.WriteAsJsonAsync(new ErrorResponse
                    {
                        message = "Invalid JSON format",
                        errorCode = "JSON_ERROR"
                    });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"UIメッセージ処理エラー: {ex.Message}");
                    context.Response.StatusCode = 500;
                    await context.Response.WriteAsJsonAsync(new ErrorResponse
                    {
                        message = "Internal server error",
                        errorCode = "INTERNAL_ERROR"
                    });
                }
            });

            // CocoroShell に、OtomeKairo 由来の実行用設定だけを返す。
            app.MapGet("/api/shell/runtime-config", async (HttpContext context) =>
            {
                if (!await AuthorizeShellAsync(context))
                {
                    return;
                }

                try
                {
                    var config = _appSettings.BuildShellRuntimeConfig();
                    context.Response.ContentType = "application/json; charset=utf-8";
                    await context.Response.WriteAsJsonAsync(config);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"CocoroShell実行設定取得エラー: {ex.Message}");
                    context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                    await context.Response.WriteAsJsonAsync(new ErrorResponse
                    {
                        message = "CocoroShellの実行設定を準備できません。",
                        errorCode = "SHELL_RUNTIME_CONFIG_UNAVAILABLE"
                    });
                }
            });

            // CocoroShell が確定したアバター位置だけを OtomeKairo の端末設定へ反映する。
            app.MapPut("/api/shell/avatar-position", async (HttpContext context) =>
            {
                if (!await AuthorizeShellAsync(context))
                {
                    return;
                }

                var previousX = _appSettings.WindowPositionX;
                var previousY = _appSettings.WindowPositionY;
                try
                {
                    var request = await context.Request.ReadFromJsonAsync<ShellAvatarPositionRequest>();
                    if (request == null)
                    {
                        throw new ArgumentException("位置を指定してください。");
                    }
                    if (!float.IsFinite(request.x) || !float.IsFinite(request.y))
                    {
                        throw new ArgumentException("位置は有限数値で指定してください。");
                    }

                    _appSettings.WindowPositionX = request.x;
                    _appSettings.WindowPositionY = request.y;

                    await _saveConsoleClientSettingsAsync(context.RequestAborted);
                    await context.Response.WriteAsJsonAsync(new StandardResponse
                    {
                        status = "success",
                        message = "Avatar position saved"
                    });
                }
                catch (ArgumentException ex)
                {
                    _appSettings.WindowPositionX = previousX;
                    _appSettings.WindowPositionY = previousY;
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    await context.Response.WriteAsJsonAsync(new ErrorResponse
                    {
                        message = ex.Message,
                        errorCode = "INVALID_AVATAR_POSITION"
                    });
                }
                catch (JsonException)
                {
                    _appSettings.WindowPositionX = previousX;
                    _appSettings.WindowPositionY = previousY;
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    await context.Response.WriteAsJsonAsync(new ErrorResponse
                    {
                        message = "Invalid JSON format",
                        errorCode = "JSON_ERROR"
                    });
                }
                catch (Exception ex)
                {
                    _appSettings.WindowPositionX = previousX;
                    _appSettings.WindowPositionY = previousY;
                    Debug.WriteLine($"アバター位置保存エラー: {ex.Message}");
                    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                    await context.Response.WriteAsJsonAsync(new ErrorResponse
                    {
                        message = "アバター位置を保存できませんでした。",
                        errorCode = "AVATAR_POSITION_SAVE_FAILED"
                    });
                }
            });

            // POST /api/control - 制御コマンド
            app.MapPost("/api/control", async (HttpContext context) =>
            {
                try
                {
                    var request = await context.Request.ReadFromJsonAsync<ControlRequest>();
                    if (request == null)
                    {
                        context.Response.StatusCode = 400;
                        await context.Response.WriteAsJsonAsync(new ErrorResponse
                        {
                            message = "Request body is required",
                            errorCode = "INVALID_REQUEST"
                        });
                        return;
                    }

                    // コマンド検証
                    var validAction = new[] { "shutdown", "restart" };
                    if (!Array.Exists(validAction, cmd => cmd == request.action))
                    {
                        context.Response.StatusCode = 400;
                        await context.Response.WriteAsJsonAsync(new ErrorResponse
                        {
                            message = $"Invalid action. Must be one of: {string.Join(", ", validAction)}",
                            errorCode = "INVALID_ACTION"
                        });
                        return;
                    }

                    // パラメータとログを出力
                    Debug.WriteLine($"制御コマンド受信: action={request.action}, reason={request.reason}, params={request.@params?.Count ?? 0}個");
                    
                    // イベント発火
                    ControlCommandReceived?.Invoke(this, request);

                    await context.Response.WriteAsJsonAsync(new StandardResponse
                    {
                        status = "success",
                        message = $"制御アクション '{request.action}' を実行しました"
                    });
                }
                catch (System.Text.Json.JsonException)
                {
                    context.Response.StatusCode = 400;
                    await context.Response.WriteAsJsonAsync(new ErrorResponse
                    {
                        message = "Invalid JSON format",
                        errorCode = "JSON_ERROR"
                    });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"制御コマンド処理エラー: {ex.Message}");
                    context.Response.StatusCode = 500;
                    await context.Response.WriteAsJsonAsync(new ErrorResponse
                    {
                        message = "Internal server error",
                        errorCode = "INTERNAL_ERROR"
                    });
                }
            });

            // POST /api/status - ステータス更新
            app.MapPost("/api/status", async (HttpContext context) =>
            {
                try
                {
                    var request = await context.Request.ReadFromJsonAsync<StatusUpdateRequest>();
                    if (request == null)
                    {
                        context.Response.StatusCode = 400;
                        await context.Response.WriteAsJsonAsync(new ErrorResponse
                        {
                            message = "Request body is required",
                            errorCode = "INVALID_REQUEST"
                        });
                        return;
                    }

                    // ステータスメッセージの検証
                    if (string.IsNullOrWhiteSpace(request.message))
                    {
                        context.Response.StatusCode = 400;
                        await context.Response.WriteAsJsonAsync(new ErrorResponse
                        {
                            message = "Field 'message' is required and cannot be empty",
                            errorCode = "VALIDATION_ERROR"
                        });
                        return;
                    }

                    // イベント発火
                    StatusUpdateReceived?.Invoke(this, request);

                    await context.Response.WriteAsJsonAsync(new StandardResponse
                    {
                        status = "success",
                        message = "Status updated"
                    });
                }
                catch (System.Text.Json.JsonException)
                {
                    context.Response.StatusCode = 400;
                    await context.Response.WriteAsJsonAsync(new ErrorResponse
                    {
                        message = "Invalid JSON format",
                        errorCode = "JSON_ERROR"
                    });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"ステータス更新処理エラー: {ex.Message}");
                    context.Response.StatusCode = 500;
                    await context.Response.WriteAsJsonAsync(new ErrorResponse
                    {
                        message = "Internal server error",
                        errorCode = "INTERNAL_ERROR"
                    });
                }
            });

        }

        /// <summary>
        /// Unity Editorへ、現在のConsole API URLと一時トークンを名前付きパイプで渡す。
        /// </summary>
        private async Task RunShellEditorBootstrapPipeAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    using var pipe = new NamedPipeServerStream(
                        ShellEditorBootstrapPipeName,
                        PipeDirection.InOut,
                        1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);

                    using var reader = new StreamReader(pipe, Encoding.UTF8, false, 1024, true);
                    using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, true)
                    {
                        AutoFlush = true
                    };
                    var request = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);

                    string response;
                    if (string.Equals(request, ShellEditorBootstrapRequest, StringComparison.Ordinal) &&
                        !string.IsNullOrEmpty(_appSettings.ShellSessionToken))
                    {
                        response = JsonSerializer.Serialize(new
                        {
                            consoleApiUrl = $"http://127.0.0.1:{_port}",
                            sessionToken = _appSettings.ShellSessionToken
                        });
                    }
                    else
                    {
                        response = JsonSerializer.Serialize(new
                        {
                            error = "shell_editor_bootstrap_unavailable"
                        });
                    }

                    await writer.WriteLineAsync(response.AsMemory(), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"CocoroShell Editor bootstrap IPCエラー: {ex.Message}");
                    try
                    {
                        await Task.Delay(500, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                }
            }
        }

        /// <summary>
        /// Console が起動した CocoroShell の一時トークンを検証する。
        /// </summary>
        private async Task<bool> AuthorizeShellAsync(HttpContext context)
        {
            const string bearerPrefix = "Bearer ";
            var expectedToken = _appSettings.ShellSessionToken;
            var authorization = context.Request.Headers["Authorization"].ToString();
            var suppliedToken = authorization.StartsWith(bearerPrefix, StringComparison.Ordinal)
                ? authorization.Substring(bearerPrefix.Length)
                : string.Empty;

            var authorized = !string.IsNullOrEmpty(expectedToken)
                && expectedToken.Length == suppliedToken.Length
                && CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(expectedToken),
                    Encoding.UTF8.GetBytes(suppliedToken));
            if (authorized)
            {
                return true;
            }

            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new ErrorResponse
            {
                message = "CocoroShellの認証に失敗しました。",
                errorCode = "INVALID_SHELL_SESSION"
            });
            return false;
        }

        /// <summary>
        /// APIサーバーを停止
        /// </summary>
        public async Task StopAsync()
        {
            if (_host == null) return;

            try
            {
                _cts?.Cancel();

                var stopTask = _host.StopAsync(TimeSpan.FromSeconds(5));
                await stopTask.ConfigureAwait(false);
                if (_shellEditorBootstrapTask != null)
                {
                    await _shellEditorBootstrapTask.ConfigureAwait(false);
                    _shellEditorBootstrapTask = null;
                }

                _host.Dispose();
                _host = null;
                _cts?.Dispose();
                _cts = null;

                Debug.WriteLine("CocoroConsole APIサーバーを停止しました");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"APIサーバー停止エラー: {ex.Message}");

                // エラーが発生してもリソースをクリーンアップ
                try { _host?.Dispose(); } catch { }
                _host = null;
                _shellEditorBootstrapTask = null;
                try { _cts?.Dispose(); } catch { }
                _cts = null;
            }
        }

        public void Dispose()
        {
            _cts?.Cancel();
            _host?.Dispose();
            _cts?.Dispose();
            _shellEditorBootstrapTask = null;
        }
    }
}
