using CocoroConsole.Models.OtomeKairoApi;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace CocoroConsole.Services
{
    /// <summary>
    /// otomekairo の HTTP API クライアント。
    /// 
    /// - Bearer 認証ヘッダを付与して JSON API を呼び出す
    /// - /api/chat は Server-Sent Events (SSE) をストリーミングとして読み取る
    /// - タイムアウト/HTTP エラーは例外として呼び出し元へ伝播する
    /// </summary>
    public class OtomeKairoApiClient : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly JsonSerializerOptions _serializerOptions;
        private bool _disposed;

        public OtomeKairoApiClient(string baseUrl, string bearerToken, HttpMessageHandler? handler = null)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                throw new ArgumentException("baseUrlを指定してください", nameof(baseUrl));
            }

            _baseUrl = baseUrl.TrimEnd('/');
            if (handler == null)
            {
                // --- OtomeKairo は自己署名HTTPSを前提とする ---
                // LAN公開（Web UI含む）に寄せるため HTTPS 必須の設計になっている。
                // CocoroConsole はローカル接続のみの前提で、証明書のホスト検証は行わない。
                handler = new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = (_, _, _, _) => true
                };
            }
            _httpClient = new HttpClient(handler);
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
            if (!string.IsNullOrWhiteSpace(bearerToken))
            {
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
            }
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            _serializerOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };
        }

        /// <summary>
        /// OtomeKairo の bootstrap 状態を取得する。
        /// </summary>
        public Task<OtomeKairoBootstrapProbeResponse> ProbeBootstrapAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            return SendOtomeKairoAsync<OtomeKairoBootstrapProbeResponse>(HttpMethod.Get, "/api/bootstrap/probe", null, cancellationToken);
        }

        /// <summary>
        /// OtomeKairo の最初の console_access_token を取得する。
        /// </summary>
        public Task<OtomeKairoRegisterFirstConsoleResponse> RegisterFirstConsoleAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            return SendOtomeKairoAsync<OtomeKairoRegisterFirstConsoleResponse>(HttpMethod.Post, "/api/bootstrap/register-first-console", new { }, cancellationToken);
        }

        /// <summary>
        /// OtomeKairo の状態要約を取得する。
        /// </summary>
        public Task<OtomeKairoStatusResponse> GetOtomeKairoStatusAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            return SendOtomeKairoAsync<OtomeKairoStatusResponse>(HttpMethod.Get, "/api/status", null, cancellationToken);
        }

        /// <summary>
        /// OtomeKairo に会話観測を送信する。
        /// </summary>
        public Task<OtomeKairoConversationResponse> ObserveConversationAsync(OtomeKairoConversationRequest request, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            return SendOtomeKairoAsync<OtomeKairoConversationResponse>(HttpMethod.Post, "/api/observations/conversation", request, cancellationToken);
        }

        /// <summary>
        /// OtomeKairo の現在設定を取得する。
        /// </summary>
        public Task<OtomeKairoConfigResponse> GetOtomeKairoConfigAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            return SendOtomeKairoAsync<OtomeKairoConfigResponse>(HttpMethod.Get, "/api/config", null, cancellationToken);
        }

        /// <summary>
        /// OtomeKairo の editor-state を取得する。
        /// </summary>
        public Task<OtomeKairoEditorState> GetEditorStateAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            return SendOtomeKairoAsync<OtomeKairoEditorState>(HttpMethod.Get, "/api/config/editor-state", null, cancellationToken);
        }

        /// <summary>
        /// OtomeKairo の editor-state を全体置換する。
        /// </summary>
        public Task<OtomeKairoEditorState> ReplaceEditorStateAsync(OtomeKairoEditorState request, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            return SendOtomeKairoAsync<OtomeKairoEditorState>(HttpMethod.Put, "/api/config/editor-state", request, cancellationToken);
        }

        /// <summary>
        /// OtomeKairo の現在設定を部分更新する。
        /// </summary>
        public Task<OtomeKairoConfigResponse> PatchCurrentConfigAsync(OtomeKairoCurrentSettingsPatch request, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            return SendOtomeKairoAsync<OtomeKairoConfigResponse>(new HttpMethod("PATCH"), "/api/config/current", request, cancellationToken);
        }

        /// <summary>
        /// /api/settings を取得する。
        /// </summary>
        public Task<OtomeKairoSettings> GetSettingsAsync(CancellationToken cancellationToken = default)
        {
            return SendAsync<OtomeKairoSettings>(HttpMethod.Get, "/api/settings", null, cancellationToken);
        }

        /// <summary>
        /// /api/settings を更新（PUT）する。
        /// </summary>
        public Task<OtomeKairoSettings> UpdateSettingsAsync(OtomeKairoSettingsUpdateRequest request, CancellationToken cancellationToken = default)
        {
            return SendAsync<OtomeKairoSettings>(HttpMethod.Put, "/api/settings", request, cancellationToken);
        }

        /// <summary>
        /// /api/reminders/settings を取得する。
        /// </summary>
        public Task<OtomeKairoRemindersSettings> GetRemindersSettingsAsync(CancellationToken cancellationToken = default)
        {
            return SendAsync<OtomeKairoRemindersSettings>(HttpMethod.Get, "/api/reminders/settings", null, cancellationToken);
        }

        /// <summary>
        /// /api/reminders/settings を更新（PUT）する。
        /// otomekairo 側が NoContent を返す実装もあるため、更新後に再取得して返す。
        /// </summary>
        public async Task<OtomeKairoRemindersSettings> UpdateRemindersSettingsAsync(OtomeKairoRemindersSettingsUpdateRequest request, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            await SendNoContentAsync(HttpMethod.Put, "/api/reminders/settings", request, cancellationToken).ConfigureAwait(false);
            return await GetRemindersSettingsAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// /api/reminders を取得する。
        /// </summary>
        public async Task<IReadOnlyList<OtomeKairoReminderItem>> GetRemindersAsync(CancellationToken cancellationToken = default)
        {
            var response = await SendAsync<OtomeKairoRemindersListResponse>(HttpMethod.Get, "/api/reminders", null, cancellationToken);
            return response.Items ?? new List<OtomeKairoReminderItem>();
        }

        /// <summary>
        /// /api/reminders を作成（POST）する。
        /// </summary>
        public Task<OtomeKairoReminderCreateResponse> CreateReminderAsync(OtomeKairoReminderCreateRequest request, CancellationToken cancellationToken = default)
        {
            return SendAsync<OtomeKairoReminderCreateResponse>(HttpMethod.Post, "/api/reminders", request, cancellationToken);
        }

        /// <summary>
        /// /api/reminders/{id} を更新（PATCH）する。
        /// </summary>
        public Task PatchReminderAsync(string reminderId, OtomeKairoReminderPatchRequest request, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            if (string.IsNullOrWhiteSpace(reminderId))
            {
                throw new ArgumentException("reminderIdを指定してください", nameof(reminderId));
            }

            return SendNoContentAsync(new HttpMethod("PATCH"), $"/api/reminders/{reminderId}", request, cancellationToken);
        }

        /// <summary>
        /// /api/reminders/{id} を削除（DELETE）する。
        /// </summary>
        public Task DeleteReminderAsync(string reminderId, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            if (string.IsNullOrWhiteSpace(reminderId))
            {
                throw new ArgumentException("reminderIdを指定してください", nameof(reminderId));
            }

            return SendNoContentAsync(HttpMethod.Delete, $"/api/reminders/{reminderId}", null, cancellationToken);
        }

        /// <summary>
        /// /api/mood/debug を取得する（管理/デバッグ）。
        /// </summary>
        public Task<MoodDebugResponse> GetMoodDebugAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            // --- mood/debug はデバッグ観測用途のため、単純に1回取得して返す ---
            return SendAsync<MoodDebugResponse>(HttpMethod.Get, "/api/mood/debug", null, cancellationToken);
        }

        /// <summary>
        /// /api/chat を SSE（text/event-stream）で呼び出し、トークン/完了/エラーを逐次返す。
        /// 
        /// - token: 返信の増分（delta）
        /// - done: 最終返信（reply_text / event_id）
        /// - error: エラー（message/code）
        /// </summary>
        public async IAsyncEnumerable<ChatStreamEvent> StreamChatAsync(ChatStreamRequest requestPayload, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            using var request = new HttpRequestMessage(HttpMethod.Post, BuildUrl("/api/chat"));
            request.Headers.Accept.Clear();
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            request.Content = CreateJsonContent(requestPayload);

            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken
            ).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                throw new HttpRequestException($"otomekairo chat APIエラー: {(int)response.StatusCode} {response.ReasonPhrase} {body}");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var reader = new StreamReader(stream, Encoding.UTF8);

            string? currentEvent = null;
            var dataBuilder = new StringBuilder();

            while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync().ConfigureAwait(false);
                if (line == null)
                {
                    break;
                }

                if (line.Length == 0)
                {
                    if (string.IsNullOrWhiteSpace(currentEvent) || dataBuilder.Length == 0)
                    {
                        currentEvent = null;
                        dataBuilder.Clear();
                        continue;
                    }

                    var json = dataBuilder.ToString();
                    dataBuilder.Clear();

                    var clientEvent = TryParseChatSseEvent(currentEvent, json);
                    currentEvent = null;

                    if (clientEvent == null)
                    {
                        continue;
                    }

                    yield return clientEvent;

                    if (clientEvent.Type == "done" || clientEvent.Type == "error")
                    {
                        yield break;
                    }

                    continue;
                }

                const string eventPrefix = "event:";
                if (line.StartsWith(eventPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    currentEvent = line.Substring(eventPrefix.Length).Trim();
                    continue;
                }

                const string dataPrefix = "data:";
                if (line.StartsWith(dataPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    if (dataBuilder.Length > 0)
                    {
                        dataBuilder.Append('\n');
                    }

                    dataBuilder.Append(line.Substring(dataPrefix.Length).Trim());
                    continue;
                }
            }
        }

        public Task SendVisionCaptureResponseAsync(VisionCaptureResponseRequest request, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            return SendNoContentAsync(HttpMethod.Post, "/api/v2/vision/capture-response", request, cancellationToken);
        }

        private async Task<T> SendOtomeKairoAsync<T>(HttpMethod method, string path, object? payload, CancellationToken cancellationToken)
        {
            var url = BuildUrl(path);
            using var request = new HttpRequestMessage(method, url);
            if (payload != null)
            {
                request.Content = CreateJsonContent(payload);
            }

            try
            {
                using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    var errorEnvelope = JsonSerializer.Deserialize<OtomeKairoErrorEnvelope>(responseBody, _serializerOptions);
                    var errorCode = errorEnvelope?.Error?.Code;
                    var errorMessage = errorEnvelope?.Error?.Message
                        ?? $"OtomeKairo APIエラー: {(int)response.StatusCode} {response.ReasonPhrase}";
                    throw new OtomeKairoApiException((int)response.StatusCode, errorCode, errorMessage);
                }

                var successEnvelope = JsonSerializer.Deserialize<OtomeKairoSuccessEnvelope<T>>(responseBody, _serializerOptions);
                if (successEnvelope == null || !successEnvelope.Ok || successEnvelope.Data == null)
                {
                    throw new InvalidOperationException("OtomeKairo APIレスポンスの解析に失敗しました");
                }

                return successEnvelope.Data;
            }
            catch (TaskCanceledException ex)
            {
                throw new TimeoutException("OtomeKairo APIリクエストがタイムアウトしました", ex);
            }
            catch (OtomeKairoApiException)
            {
                throw;
            }
            catch (HttpRequestException ex)
            {
                throw new InvalidOperationException($"OtomeKairo API通信に失敗しました: {ex.Message}", ex);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"OtomeKairo API通信に失敗しました: {ex.Message}", ex);
            }
        }

        private async Task SendNoContentAsync(HttpMethod method, string path, object? payload, CancellationToken cancellationToken)
        {
            var url = BuildUrl(path);
            using var request = new HttpRequestMessage(method, url);
            if (payload != null)
            {
                request.Content = CreateJsonContent(payload);
            }

            try
            {
                using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException($"otomekairo APIエラー: {(int)response.StatusCode} {response.ReasonPhrase} {responseBody}");
                }
            }
            catch (TaskCanceledException ex)
            {
                throw new TimeoutException("otomekairo APIリクエストがタイムアウトしました", ex);
            }
            catch (HttpRequestException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"otomekairo API通信に失敗しました: {ex.Message}", ex);
            }
        }

        private async Task<T> SendAsync<T>(HttpMethod method, string path, object? payload, CancellationToken cancellationToken)
        {
            var url = BuildUrl(path);
            var request = new HttpRequestMessage(method, url);
            if (payload != null)
            {
                request.Content = CreateJsonContent(payload);
            }

            try
            {
                using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException($"otomekairo APIエラー: {(int)response.StatusCode} {response.ReasonPhrase} {responseBody}");
                }

                var result = JsonSerializer.Deserialize<T>(responseBody, _serializerOptions);
                if (result == null)
                {
                    throw new InvalidOperationException("otomekairo APIレスポンスの解析に失敗しました");
                }

                return result;
            }
            catch (TaskCanceledException ex)
            {
                throw new TimeoutException("otomekairo APIリクエストがタイムアウトしました", ex);
            }
            catch (HttpRequestException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"otomekairo API通信に失敗しました: {ex.Message}", ex);
            }
            finally
            {
                request.Dispose();
            }
        }

        private StringContent CreateJsonContent(object payload)
        {
            var json = JsonSerializer.Serialize(payload, _serializerOptions);
            return new StringContent(json, Encoding.UTF8, "application/json");
        }

        private string BuildUrl(string path)
        {
            return $"{_baseUrl}/{path.TrimStart('/')}";
        }

        private ChatStreamEvent? TryParseChatSseEvent(string eventName, string json)
        {
            // SSE event の data 行を JSON として解釈し、クライアント側の統一イベントに変換する
            if (string.IsNullOrWhiteSpace(eventName) || string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                switch (eventName)
                {
                    case "token":
                        {
                            var payload = JsonSerializer.Deserialize<ChatStreamTokenPayload>(json, _serializerOptions);
                            if (string.IsNullOrEmpty(payload?.Text))
                            {
                                return null;
                            }

                            return new ChatStreamEvent
                            {
                                Type = "token",
                                Delta = payload.Text
                            };
                        }
                    case "done":
                        {
                            var payload = JsonSerializer.Deserialize<ChatStreamDonePayload>(json, _serializerOptions);
                            if (payload == null)
                            {
                                return null;
                            }

                            return new ChatStreamEvent
                            {
                                Type = "done",
                                ReplyText = payload.ReplyText,
                                EventId = payload.EventId
                            };
                        }
                    case "error":
                        {
                            var payload = JsonSerializer.Deserialize<ChatStreamErrorPayload>(json, _serializerOptions);
                            return new ChatStreamEvent
                            {
                                Type = "error",
                                ErrorMessage = payload?.Message,
                                ErrorCode = payload?.Code
                            };
                        }
                    default:
                        return null;
                }
            }
            catch
            {
                return null;
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _httpClient.Dispose();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(OtomeKairoApiClient));
            }
        }
    }

    public class OtomeKairoApiException : Exception
    {
        public int StatusCode { get; }
        public string? ErrorCode { get; }

        public OtomeKairoApiException(int statusCode, string? errorCode, string message)
            : base(message)
        {
            StatusCode = statusCode;
            ErrorCode = errorCode;
        }
    }

    internal class OtomeKairoSuccessEnvelope<T>
    {
        [JsonPropertyName("ok")]
        public bool Ok { get; set; }

        [JsonPropertyName("data")]
        public T? Data { get; set; }
    }

    internal class OtomeKairoErrorEnvelope
    {
        [JsonPropertyName("ok")]
        public bool Ok { get; set; }

        [JsonPropertyName("error")]
        public OtomeKairoErrorBody? Error { get; set; }
    }

    internal class OtomeKairoErrorBody
    {
        [JsonPropertyName("code")]
        public string? Code { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }
    }

    public class OtomeKairoBootstrapProbeResponse
    {
        [JsonPropertyName("bootstrap_available")]
        public bool BootstrapAvailable { get; set; }

        [JsonPropertyName("https_required")]
        public bool HttpsRequired { get; set; }

        [JsonPropertyName("bootstrap_state")]
        public string BootstrapState { get; set; } = string.Empty;
    }

    public class OtomeKairoRegisterFirstConsoleResponse
    {
        [JsonPropertyName("console_access_token")]
        public string ConsoleAccessToken { get; set; } = string.Empty;
    }

    public class OtomeKairoStatusResponse
    {
        [JsonPropertyName("settings_snapshot")]
        public Dictionary<string, object?> SettingsSnapshot { get; set; } = new Dictionary<string, object?>();

        [JsonPropertyName("runtime_summary")]
        public Dictionary<string, object?> RuntimeSummary { get; set; } = new Dictionary<string, object?>();
    }

    public class OtomeKairoConfigResponse
    {
        [JsonPropertyName("settings_snapshot")]
        public OtomeKairoCurrentSettings SettingsSnapshot { get; set; } = new OtomeKairoCurrentSettings();

        [JsonPropertyName("selected_persona")]
        public OtomeKairoPersonaDefinition SelectedPersona { get; set; } = new OtomeKairoPersonaDefinition();

        [JsonPropertyName("selected_memory_set")]
        public OtomeKairoMemorySetDefinition SelectedMemorySet { get; set; } = new OtomeKairoMemorySetDefinition();

        [JsonPropertyName("selected_model_preset")]
        public OtomeKairoModelPresetDefinition SelectedModelPreset { get; set; } = new OtomeKairoModelPresetDefinition();

        [JsonPropertyName("selected_model_profile_ids")]
        public Dictionary<string, string> SelectedModelProfileIds { get; set; } = new Dictionary<string, string>();
    }

    public class OtomeKairoCurrentSettingsPatch
    {
        [JsonPropertyName("selected_persona_id")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? SelectedPersonaId { get; set; }

        [JsonPropertyName("selected_memory_set_id")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? SelectedMemorySetId { get; set; }

        [JsonPropertyName("selected_model_preset_id")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? SelectedModelPresetId { get; set; }

        [JsonPropertyName("memory_enabled")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? MemoryEnabled { get; set; }

        [JsonPropertyName("desktop_watch")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public OtomeKairoDesktopWatchSettings? DesktopWatch { get; set; }

        [JsonPropertyName("wake_policy")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Dictionary<string, object?>? WakePolicy { get; set; }
    }

    public class OtomeKairoConversationRequest
    {
        [JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;

        [JsonPropertyName("client_context")]
        public Dictionary<string, object?>? ClientContext { get; set; }
    }

    public class OtomeKairoConversationResponse
    {
        [JsonPropertyName("cycle_id")]
        public string CycleId { get; set; } = string.Empty;

        [JsonPropertyName("result_kind")]
        public string ResultKind { get; set; } = string.Empty;

        [JsonPropertyName("reply")]
        public OtomeKairoConversationReply? Reply { get; set; }
    }

    public class OtomeKairoConversationReply
    {
        [JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;
    }

    /// <summary>
    /// /api/chat へのリクエスト DTO。
    /// </summary>
    public class ChatStreamRequest
    {
        /// <summary>
        /// 入力テキスト。
        /// 
        /// - 省略/空文字列を許可する
        /// - 空で画像がある場合、サーバ側で内部的に補完される
        /// </summary>
        [JsonPropertyName("input_text")]
        public string InputText { get; set; } = string.Empty;

        /// <summary>
        /// 画像（Data URI）一覧。
        /// 
        /// - 例: "data:image/png;base64,...."
        /// - otomekairo 側で MIME/サイズ上限を検証する
        /// </summary>
        [JsonPropertyName("images")]
        public List<string>? Images { get; set; }

        /// <summary>
        /// クライアント側のコンテキスト情報（アクティブアプリ等）。
        /// </summary>
        [JsonPropertyName("client_context")]
        public VisionClientContext? ClientContext { get; set; }
    }

    /// <summary>
    /// /api/chat の SSE をクライアント側で扱いやすい形に正規化したイベント。
    /// </summary>
    public class ChatStreamEvent
    {
        /// <summary>
        /// "token" | "done" | "error"。
        /// </summary>
        public string Type { get; set; } = string.Empty;

        /// <summary>
        /// token イベント時の増分テキスト。
        /// </summary>
        public string? Delta { get; set; }

        /// <summary>
        /// done イベント時の最終返信。
        /// </summary>
        public string? ReplyText { get; set; }

        /// <summary>
        /// done イベント時の event_id（otomekairo の events.event_id）。
        /// </summary>
        public int? EventId { get; set; }

        /// <summary>
        /// error イベント時のメッセージ。
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// error イベント時のエラーコード（例: chat_busy / invalid_request）。
        /// </summary>
        public string? ErrorCode { get; set; }
    }

    internal class ChatStreamTokenPayload
    {
        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }

    internal class ChatStreamDonePayload
    {
        [JsonPropertyName("event_id")]
        public int? EventId { get; set; }

        [JsonPropertyName("reply_text")]
        public string? ReplyText { get; set; }
    }

    internal class ChatStreamErrorPayload
    {
        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("code")]
        public string? Code { get; set; }
    }

    /// <summary>
    /// Vision/Chat で送るクライアント側の状況スナップショット。
    /// </summary>
    public class VisionClientContext
    {
        [JsonPropertyName("active_app")]
        public string? ActiveApp { get; set; }

        [JsonPropertyName("window_title")]
        public string? WindowTitle { get; set; }

        [JsonPropertyName("locale")]
        public string? Locale { get; set; }
    }

    public class VisionCaptureResponseRequest
    {
        /// <summary>
        /// 画像キャプチャ要求 ID。
        /// </summary>
        [JsonPropertyName("request_id")]
        public string RequestId { get; set; } = string.Empty;

        /// <summary>
        /// 返信するクライアント ID。
        /// </summary>
        [JsonPropertyName("client_id")]
        public string ClientId { get; set; } = string.Empty;

        [JsonPropertyName("images")]
        public List<string> Images { get; set; } = new List<string>();

        [JsonPropertyName("client_context")]
        public VisionClientContext? ClientContext { get; set; }

        [JsonPropertyName("error")]
        public string? Error { get; set; }
    }
}
