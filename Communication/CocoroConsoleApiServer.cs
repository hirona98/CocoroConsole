using CocoroConsole.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
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
        private IHost? _host;
        private readonly int _port;
        private readonly IAppSettings _appSettings;
        private readonly Func<CancellationToken, Task> _saveConsoleClientSettingsAsync;
        private CancellationTokenSource? _cts;

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
        public Task StartAsync()
        {
            if (_host != null) return Task.CompletedTask;

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

                // バックグラウンドでサーバーを起動
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _host.RunAsync(_cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        // 正常な終了
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"APIサーバー実行エラー: {ex.Message}");
                    }
                });

                Debug.WriteLine($"CocoroConsole APIサーバーを起動しました: http://127.0.0.1:{_port}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"APIサーバー起動エラー: {ex.Message}");
                throw new InvalidOperationException($"APIサーバーの起動に失敗しました: {ex.Message}", ex);
            }

            return Task.CompletedTask;
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

            // GET /api/config - 設定取得
            app.MapGet("/api/config", async (HttpContext context) =>
            {
                try
                {
                    var config = _appSettings.GetConfigSettings();
                    context.Response.ContentType = "application/json; charset=utf-8";
                    await context.Response.WriteAsJsonAsync(config);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"設定取得エラー: {ex.Message}");
                    context.Response.StatusCode = 500;
                    await context.Response.WriteAsJsonAsync(new ErrorResponse
                    {
                        message = "Failed to retrieve configuration",
                        errorCode = "CONFIG_ERROR"
                    });
                }
            });

            // CocoroShell が確定したアバター位置だけを OtomeKairo の端末設定へ反映する。
            app.MapPut("/api/config/patch", async (HttpContext context) =>
            {
                var previousX = _appSettings.WindowPositionX;
                var previousY = _appSettings.WindowPositionY;
                try
                {
                    var patch = await context.Request.ReadFromJsonAsync<ConfigPatchRequest>();
                    if (patch == null || patch.updates.Count == 0)
                    {
                        throw new ArgumentException("updatesを指定してください。");
                    }

                    foreach (var fieldName in patch.updates.Keys)
                    {
                        if (fieldName != "windowPositionX" && fieldName != "windowPositionY")
                        {
                            throw new ArgumentException($"未対応の設定項目です: {fieldName}");
                        }
                    }

                    if (patch.updates.TryGetValue("windowPositionX", out var x))
                    {
                        _appSettings.WindowPositionX = ReadFiniteSingle(x, "windowPositionX");
                    }
                    if (patch.updates.TryGetValue("windowPositionY", out var y))
                    {
                        _appSettings.WindowPositionY = ReadFiniteSingle(y, "windowPositionY");
                    }

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
                        errorCode = "INVALID_CONFIG_PATCH"
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
                        message = "Failed to save avatar position",
                        errorCode = "CONFIG_PATCH_ERROR"
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
                    var validAction = new[] { "shutdown", "restart", "reloadConfig" };
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

        private static float ReadFiniteSingle(object value, string fieldName)
        {
            if (value is not JsonElement element
                || element.ValueKind != JsonValueKind.Number
                || !element.TryGetSingle(out var number)
                || !float.IsFinite(number))
            {
                throw new ArgumentException($"{fieldName}には有限数値を指定してください。");
            }
            return number;
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
                try { _cts?.Dispose(); } catch { }
                _cts = null;
            }
        }

        public void Dispose()
        {
            _cts?.Cancel();
            _host?.Dispose();
            _cts?.Dispose();
        }
    }
}
