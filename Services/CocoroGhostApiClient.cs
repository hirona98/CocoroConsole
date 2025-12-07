using CocoroConsole.Models.CocoroGhostApi;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
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
                PropertyNameCaseInsensitive = true
            };
        }

        public Task<CocoroGhostSettings> GetSettingsAsync(CancellationToken cancellationToken = default)
        {
            return SendAsync<CocoroGhostSettings>(HttpMethod.Get, "/api/settings", null, cancellationToken);
        }

        public Task<CocoroGhostSettings> UpdateSettingsAsync(CocoroGhostSettingsUpdateRequest request, CancellationToken cancellationToken = default)
        {
            return SendAsync<CocoroGhostSettings>(HttpMethod.Post, "/api/settings", request, cancellationToken);
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

            while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync().ConfigureAwait(false);
                if (line == null)
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                const string dataPrefix = "data:";
                if (!line.StartsWith(dataPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var json = line.Substring(dataPrefix.Length).Trim();
                if (string.IsNullOrWhiteSpace(json))
                {
                    continue;
                }

                ChatStreamServerEvent? serverEvent = null;
                try
                {
                    serverEvent = JsonSerializer.Deserialize<ChatStreamServerEvent>(json, _serializerOptions);
                }
                catch
                {
                    // SSEフォーマットが想定外の場合はスキップ
                    continue;
                }

                if (serverEvent == null || string.IsNullOrWhiteSpace(serverEvent.Type))
                {
                    continue;
                }

                var clientEvent = new ChatStreamEvent
                {
                    Type = serverEvent.Type,
                    Delta = serverEvent.Delta,
                    ReplyText = serverEvent.ReplyText,
                    EpisodeId = serverEvent.EpisodeId,
                    ErrorMessage = serverEvent.Message
                };

                yield return clientEvent;

                if (serverEvent.Type == "done" || serverEvent.Type == "error")
                {
                    yield break;
                }
            }
        }

        private async Task<T> SendAsync<T>(HttpMethod method, string path, object? payload, CancellationToken cancellationToken)
        {
            var request = new HttpRequestMessage(method, BuildUrl(path));

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
        public string UserId { get; set; } = "default";

        [JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;

        [JsonPropertyName("context_hint")]
        public string? ContextHint { get; set; }

        [JsonPropertyName("image_base64")]
        public string? ImageBase64 { get; set; }
    }

    public class ChatStreamEvent
    {
        public string Type { get; set; } = string.Empty;
        public string? Delta { get; set; }
        public string? ReplyText { get; set; }
        public int? EpisodeId { get; set; }
        public string? ErrorMessage { get; set; }
    }

    internal class ChatStreamServerEvent
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("delta")]
        public string? Delta { get; set; }

        [JsonPropertyName("reply_text")]
        public string? ReplyText { get; set; }

        [JsonPropertyName("episode_id")]
        public int? EpisodeId { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }
    }
}
