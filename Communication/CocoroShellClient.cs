using System;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CocoroConsole.Communication
{
    /// <summary>
    /// CocoroShell REST APIクライアント
    /// </summary>
    public class CocoroShellClient : IDisposable
    {
        private readonly HttpClient _httpClient;
        private bool _disposed;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="baseUrl">ベースURL（例: http://127.0.0.1:55605）</param>
        public CocoroShellClient(string baseUrl)
        {
            _httpClient = new HttpClient
            {
                BaseAddress = new Uri(baseUrl),
                Timeout = TimeSpan.FromSeconds(30)
            };
            _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
        }

        /// <summary>
        /// コンストラクタ（ポート番号指定）
        /// </summary>
        /// <param name="port">ポート番号</param>
        public CocoroShellClient(int port) : this($"http://127.0.0.1:{port}")
        {
        }

        /// <summary>
        /// CocoroShellが応答可能な状態か確認
        /// </summary>
        public async Task<bool> IsRunningAsync()
        {
            try
            {
                using var response = await _httpClient.GetAsync("/api/status").ConfigureAwait(false);
                // HTTP応答が返ればShellのloopback API processは存在する。
                // status codeの異常は後続のWAV配送でエラーとして扱う。
                return true;
            }
            catch (Exception ex) when (ex is TaskCanceledException || ex is HttpRequestException)
            {
                return false;
            }
        }

        /// <summary>
        /// OtomeKairoで合成済みのWAVをCocoroShellへ送信
        /// </summary>
        public async Task<StandardResponse> SendAudioAsync(
            byte[] wavBytes,
            string sessionToken,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(wavBytes);
            if (wavBytes.Length == 0)
            {
                throw new ArgumentException("WAVが空です。", nameof(wavBytes));
            }
            if (string.IsNullOrEmpty(sessionToken))
            {
                throw new InvalidOperationException("CocoroShellのセッショントークンがありません。");
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/audio/playback");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sessionToken);
            request.Content = new ByteArrayContent(wavBytes);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");

            using var response = await _httpClient
                .SendAsync(request, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var errorResponse = await TryReadErrorResponse(response).ConfigureAwait(false);
                throw new HttpRequestException(
                    $"API error: {errorResponse?.message ?? response.ReasonPhrase}");
            }
            var result = await response.Content
                .ReadFromJsonAsync<StandardResponse>(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return result ?? new StandardResponse
            {
                status = "success",
                message = "Audio accepted"
            };
        }

        /// <summary>
        /// アニメーションコマンドを送信
        /// </summary>
        public async Task<StandardResponse> SendAnimationCommandAsync(AnimationRequest request)
        {
            try
            {
                var response = await _httpClient.PostAsJsonAsync("/api/animation", request);

                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<StandardResponse>();
                    return result ?? new StandardResponse
                    {
                        status = "success",
                        message = "Animation sent"
                    };
                }
                else
                {
                    var errorResponse = await TryReadErrorResponse(response);
                    throw new HttpRequestException($"API error: {errorResponse?.message ?? response.ReasonPhrase}");
                }
            }
            catch (TaskCanceledException)
            {
                throw new TimeoutException("Request to CocoroShell timed out");
            }
            catch (HttpRequestException)
            {
                throw; // そのまま再スロー
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"アニメーションアクション送信エラー: {ex.Message}");
                throw new InvalidOperationException($"Failed to send animation action: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 制御コマンドを送信
        /// </summary>
        public async Task<StandardResponse> SendControlCommandAsync(ShellControlRequest request)
        {
            try
            {
                var response = await _httpClient.PostAsJsonAsync("/api/control", request);

                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<StandardResponse>();
                    return result ?? new StandardResponse
                    {
                        status = "success",
                        message = "Control action sent"
                    };
                }
                else
                {
                    var errorResponse = await TryReadErrorResponse(response);
                    throw new HttpRequestException($"API error: {errorResponse?.message ?? response.ReasonPhrase}");
                }
            }
            catch (TaskCanceledException)
            {
                throw new TimeoutException("Request to CocoroShell timed out");
            }
            catch (HttpRequestException)
            {
                throw; // そのまま再スロー
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"制御コマンド送信エラー: {ex.Message}");
                throw new InvalidOperationException($"Failed to send control action: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 現在のアバター位置を取得
        /// </summary>
        public async Task<PositionResponse> GetPositionAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync("/api/position");

                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<PositionResponse>();
                    return result ?? throw new InvalidOperationException("Failed to deserialize position response");
                }
                else
                {
                    var errorResponse = await TryReadErrorResponse(response);
                    throw new HttpRequestException($"API error: {errorResponse?.message ?? response.ReasonPhrase}");
                }
            }
            catch (TaskCanceledException)
            {
                throw new TimeoutException("Request to CocoroShell timed out");
            }
            catch (HttpRequestException)
            {
                throw; // そのまま再スロー
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"位置取得エラー: {ex.Message}");
                throw new InvalidOperationException($"Failed to get position: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// エラーレスポンスの読み取りを試みる
        /// </summary>
        private async Task<ErrorResponse?> TryReadErrorResponse(HttpResponseMessage response)
        {
            try
            {
                var content = await response.Content.ReadAsStringAsync();
                if (!string.IsNullOrWhiteSpace(content))
                {
                    return JsonSerializer.Deserialize<ErrorResponse>(content, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"エラーレスポンス読み取りエラー: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// リソースの解放
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// リソースの解放（内部実装）
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _httpClient?.Dispose();
                }
                _disposed = true;
            }
        }
    }
}
