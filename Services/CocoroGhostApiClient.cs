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

        public Task<CocoroGhostSettings> GetSettingsAsync(CancellationToken cancellationToken = default)
        {
            return SendAsync<CocoroGhostSettings>(HttpMethod.Get, "/api/settings", null, cancellationToken);
        }

        public Task<CocoroGhostSettings> UpdateSettingsAsync(CocoroGhostSettingsUpdateRequest request, CancellationToken cancellationToken = default)
        {
            return SendAsync<CocoroGhostSettings>(HttpMethod.Put, "/api/settings", request, cancellationToken);
        }

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

        public Task<CaptureResponse> SendCaptureAsync(CaptureRequest request, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            return SendAsync<CaptureResponse>(HttpMethod.Post, "/api/capture", request, cancellationToken);
        }

        public Task<OtomeKairoState> GetOtomeKairoAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            return SendAsync<OtomeKairoState>(HttpMethod.Get, "/api/otome_kairo", null, cancellationToken);
        }

        public Task<OtomeKairoState> UpdateOtomeKairoOverrideAsync(OtomeKairoOverrideRequest request, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            return SendAsync<OtomeKairoState>(HttpMethod.Put, "/api/otome_kairo", request, cancellationToken);
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
                                EpisodeId = payload.EpisodeUnitId
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

    public class ChatStreamRequest
    {
        [JsonPropertyName("user_id")]
        [Obsolete("user_idは非推奨です。memory_id / user_text / images を使用してください。")]
        public string? UserId { get; set; }

        [JsonPropertyName("memory_id")]
        public string? MemoryId { get; set; }

        [JsonPropertyName("user_text")]
        public string UserText { get; set; } = string.Empty;

        [JsonPropertyName("images")]
        public List<CocoroGhostImage> Images { get; set; } = new List<CocoroGhostImage>();

        [JsonPropertyName("client_context")]
        public Dictionary<string, object?>? ClientContext { get; set; }
    }

    public class ChatStreamEvent
    {
        public string Type { get; set; } = string.Empty;
        public string? Delta { get; set; }
        public string? ReplyText { get; set; }
        public int? EpisodeId { get; set; }
        public string? ErrorMessage { get; set; }
    }

    internal class ChatStreamTokenPayload
    {
        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }

    internal class ChatStreamDonePayload
    {
        [JsonPropertyName("episode_unit_id")]
        public int? EpisodeUnitId { get; set; }

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
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("base64")]
        public string Base64 { get; set; } = string.Empty;
    }

    public class CaptureRequest
    {
        [JsonPropertyName("capture_type")]
        public string CaptureType { get; set; } = string.Empty;

        [JsonPropertyName("image_base64")]
        public string ImageBase64 { get; set; } = string.Empty;

        [JsonPropertyName("context_text")]
        public string? ContextText { get; set; }
    }

    public class CaptureResponse
    {
        [JsonPropertyName("episode_id")]
        public int EpisodeId { get; set; }

        [JsonPropertyName("stored")]
        public bool Stored { get; set; }
    }
}
