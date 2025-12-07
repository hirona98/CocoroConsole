using CocoroConsole.Models.CocoroGhostApi;
using System;
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
            return SendAsync<CocoroGhostSettings>(HttpMethod.Post, "/api/settings", request, cancellationToken);
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
    }
}
