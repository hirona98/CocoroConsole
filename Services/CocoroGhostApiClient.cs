using CocoroConsole.Models.CocoroGhostApi;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
    /// cocoro_ghost の HTTP API クライアント。
    /// 
    /// - Bearer 認証ヘッダを付与して JSON API を呼び出す
    /// - /api/chat は Server-Sent Events (SSE) をストリーミングとして読み取る
    /// - タイムアウト/HTTP エラーは例外として呼び出し元へ伝播する
    /// </summary>
    public class CocoroGhostApiClient : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly JsonSerializerOptions _serializerOptions;
        private bool _disposed;

        public CocoroGhostApiClient(string baseUrl, string bearerToken, HttpMessageHandler? handler = null)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                throw new ArgumentException("baseUrlを指定してください", nameof(baseUrl));
            }

            if (string.IsNullOrWhiteSpace(bearerToken))
            {
                throw new ArgumentException("Bearerトークンを指定してください", nameof(bearerToken));
            }

            _baseUrl = baseUrl.TrimEnd('/');
            _httpClient = handler != null ? new HttpClient(handler) : new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            _serializerOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };
        }

        /// <summary>
        /// /api/settings を取得する。
        /// </summary>
        public Task<CocoroGhostSettings> GetSettingsAsync(CancellationToken cancellationToken = default)
        {
            return SendAsync<CocoroGhostSettings>(HttpMethod.Get, "/api/settings", null, cancellationToken);
        }

        /// <summary>
        /// /api/settings を更新（PUT）する。
        /// </summary>
        public Task<CocoroGhostSettings> UpdateSettingsAsync(CocoroGhostSettingsUpdateRequest request, CancellationToken cancellationToken = default)
        {
            return SendAsync<CocoroGhostSettings>(HttpMethod.Put, "/api/settings", request, cancellationToken);
        }

        /// <summary>
        /// /api/reminders/settings を取得する。
        /// </summary>
        public Task<CocoroGhostRemindersSettings> GetRemindersSettingsAsync(CancellationToken cancellationToken = default)
        {
            return SendAsync<CocoroGhostRemindersSettings>(HttpMethod.Get, "/api/reminders/settings", null, cancellationToken);
        }

        /// <summary>
        /// /api/reminders/settings を更新（PUT）する。
        /// cocoro_ghost 側が NoContent を返す実装もあるため、更新後に再取得して返す。
        /// </summary>
        public async Task<CocoroGhostRemindersSettings> UpdateRemindersSettingsAsync(CocoroGhostRemindersSettingsUpdateRequest request, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            await SendNoContentAsync(HttpMethod.Put, "/api/reminders/settings", request, cancellationToken).ConfigureAwait(false);
            return await GetRemindersSettingsAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// /api/reminders を取得する。
        /// </summary>
        public async Task<IReadOnlyList<CocoroGhostReminderItem>> GetRemindersAsync(CancellationToken cancellationToken = default)
        {
            var response = await SendAsync<CocoroGhostRemindersListResponse>(HttpMethod.Get, "/api/reminders", null, cancellationToken);
            return response.Items ?? new List<CocoroGhostReminderItem>();
        }

        /// <summary>
        /// /api/reminders を作成（POST）する。
        /// </summary>
        public Task<CocoroGhostReminderCreateResponse> CreateReminderAsync(CocoroGhostReminderCreateRequest request, CancellationToken cancellationToken = default)
        {
            return SendAsync<CocoroGhostReminderCreateResponse>(HttpMethod.Post, "/api/reminders", request, cancellationToken);
        }

        /// <summary>
        /// /api/reminders/{id} を更新（PATCH）する。
        /// </summary>
        public Task PatchReminderAsync(string reminderId, CocoroGhostReminderPatchRequest request, CancellationToken cancellationToken = default)
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
                throw new HttpRequestException($"cocoro_ghost chat APIエラー: {(int)response.StatusCode} {response.ReasonPhrase} {body}");
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
                    throw new HttpRequestException($"cocoro_ghost APIエラー: {(int)response.StatusCode} {response.ReasonPhrase} {responseBody}");
                }
            }
            catch (TaskCanceledException ex)
            {
                throw new TimeoutException("cocoro_ghost APIリクエストがタイムアウトしました", ex);
            }
            catch (HttpRequestException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"cocoro_ghost API通信に失敗しました: {ex.Message}", ex);
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
                    throw new HttpRequestException($"cocoro_ghost APIエラー: {(int)response.StatusCode} {response.ReasonPhrase} {responseBody}");
                }

                var result = JsonSerializer.Deserialize<T>(responseBody, _serializerOptions);
                if (result == null)
                {
                    throw new InvalidOperationException("cocoro_ghost APIレスポンスの解析に失敗しました");
                }

                return result;
            }
            catch (TaskCanceledException ex)
            {
                throw new TimeoutException("cocoro_ghost APIリクエストがタイムアウトしました", ex);
            }
            catch (HttpRequestException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"cocoro_ghost API通信に失敗しました: {ex.Message}", ex);
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
                                ErrorMessage = payload?.Message
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
                throw new ObjectDisposedException(nameof(CocoroGhostApiClient));
            }
        }
    }

    /// <summary>
    /// /api/chat へのリクエスト DTO。
    /// </summary>
    public class ChatStreamRequest
    {
        /// <summary>
        /// 利用する埋め込みプリセット ID。
        /// </summary>
        [JsonPropertyName("embedding_preset_id")]
        public string EmbeddingPresetId { get; set; } = string.Empty;

        /// <summary>
        /// クライアント ID（CocoroConsole など）。
        /// </summary>
        [JsonPropertyName("client_id")]
        public string ClientId { get; set; } = string.Empty;

        /// <summary>
        /// 入力テキスト。
        /// </summary>
        [JsonPropertyName("input_text")]
        public string InputText { get; set; } = string.Empty;

        /// <summary>
        /// 画像（base64）一覧。
        /// </summary>
        [JsonPropertyName("images")]
        public List<CocoroGhostImage> Images { get; set; } = new List<CocoroGhostImage>();

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
        /// done イベント時の event_id（cocoro_ghost の events.event_id）。
        /// </summary>
        public int? EventId { get; set; }

        /// <summary>
        /// error イベント時のメッセージ。
        /// </summary>
        public string? ErrorMessage { get; set; }
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

    public class CocoroGhostImage
    {
        /// <summary>
        /// 画像種別（例: "image"）。
        /// </summary>
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        /// <summary>
        /// base64 エンコードされた画像データ（data URL ではなく raw base64 を想定）。
        /// </summary>
        [JsonPropertyName("base64")]
        public string Base64 { get; set; } = string.Empty;
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
