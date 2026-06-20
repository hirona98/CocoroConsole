using CocoroConsole.Models.OtomeKairoApi;
using System;
using System.Collections.Generic;
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
    /// </summary>
    public class OtomeKairoApiClient : IDisposable
    {
        private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromMinutes(3);
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
                handler = new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
                };
            }

            _httpClient = new HttpClient(handler)
            {
                Timeout = Timeout.InfiniteTimeSpan,
            };
            if (!string.IsNullOrWhiteSpace(bearerToken))
            {
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
            }
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            _serializerOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            };
        }

        public void SetBearerToken(string bearerToken)
        {
            ThrowIfDisposed();

            _httpClient.DefaultRequestHeaders.Authorization = string.IsNullOrWhiteSpace(bearerToken)
                ? null
                : new AuthenticationHeaderValue("Bearer", bearerToken.Trim());
        }

        public Task<OtomeKairoBootstrapProbeResponse> ProbeBootstrapAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            return SendOtomeKairoAsync<OtomeKairoBootstrapProbeResponse>(HttpMethod.Get, "/api/bootstrap/probe", null, cancellationToken);
        }

        public Task<OtomeKairoRegisterFirstConsoleResponse> RegisterFirstConsoleAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            return SendOtomeKairoAsync<OtomeKairoRegisterFirstConsoleResponse>(HttpMethod.Post, "/api/bootstrap/register-first-console", new { }, cancellationToken);
        }

        public Task<OtomeKairoStatusResponse> GetOtomeKairoStatusAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            return SendOtomeKairoAsync<OtomeKairoStatusResponse>(HttpMethod.Get, "/api/status", null, cancellationToken);
        }

        public Task<OtomeKairoConversationResponse> SendConversationAsync(OtomeKairoConversationRequest request, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            return SendOtomeKairoAsync<OtomeKairoConversationResponse>(HttpMethod.Post, "/api/conversation", request, cancellationToken);
        }

        public Task<OtomeKairoConfigResponse> GetOtomeKairoConfigAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            return SendOtomeKairoAsync<OtomeKairoConfigResponse>(HttpMethod.Get, "/api/config", null, cancellationToken);
        }

        public Task<OtomeKairoEditorState> GetEditorStateAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            return SendOtomeKairoAsync<OtomeKairoEditorState>(HttpMethod.Get, "/api/config/editor-state", null, cancellationToken);
        }

        public Task<OtomeKairoEditorState> ReplaceEditorStateAsync(OtomeKairoEditorState request, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            return SendOtomeKairoAsync<OtomeKairoEditorState>(HttpMethod.Put, "/api/config/editor-state", request, cancellationToken);
        }

        public Task<OtomeKairoCameraSourcesEditorState> GetCameraSourcesEditorStateAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            return SendOtomeKairoAsync<OtomeKairoCameraSourcesEditorState>(
                HttpMethod.Get,
                "/api/config/camera-sources/editor-state",
                null,
                cancellationToken);
        }

        public Task<OtomeKairoCameraSourcesEditorState> ReplaceCameraSourcesEditorStateAsync(
            OtomeKairoCameraSourcesEditorState request,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            return SendOtomeKairoAsync<OtomeKairoCameraSourcesEditorState>(
                HttpMethod.Put,
                "/api/config/camera-sources/editor-state",
                request,
                cancellationToken);
        }

        public Task<OtomeKairoMcpServersEditorState> GetMcpServersEditorStateAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            return SendOtomeKairoAsync<OtomeKairoMcpServersEditorState>(
                HttpMethod.Get,
                "/api/config/mcp-servers/editor-state",
                null,
                cancellationToken);
        }

        public Task<OtomeKairoMcpServersEditorState> ReplaceMcpServersEditorStateAsync(
            OtomeKairoMcpServersEditorState request,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            return SendOtomeKairoAsync<OtomeKairoMcpServersEditorState>(
                HttpMethod.Put,
                "/api/config/mcp-servers/editor-state",
                request,
                cancellationToken);
        }

        public Task ReplaceMemorySetAsync(OtomeKairoMemorySetDefinition request, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            return SendOkAsync(
                HttpMethod.Put,
                BuildConfigResourcePath("/api/config/memory-sets", request.MemorySetId),
                request,
                cancellationToken);
        }

        public Task CloneMemorySetAsync(
            string sourceMemorySetId,
            string targetMemorySetId,
            string displayName,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            return SendOkAsync(
                HttpMethod.Post,
                "/api/config/memory-sets/clone",
                new
                {
                    source_memory_set_id = sourceMemorySetId,
                    memory_set_id = targetMemorySetId,
                    display_name = displayName,
                },
                cancellationToken);
        }

        public Task DeleteMemorySetAsync(string memorySetId, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            return SendOkAsync(
                HttpMethod.Delete,
                BuildConfigResourcePath("/api/config/memory-sets", memorySetId),
                null,
                cancellationToken);
        }

        public Task<OtomeKairoConfigResponse> PatchCurrentConfigAsync(OtomeKairoCurrentSettingsPatch request, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            return SendOtomeKairoAsync<OtomeKairoConfigResponse>(new HttpMethod("PATCH"), "/api/config/current", request, cancellationToken);
        }

        public Task PatchCapabilityStateAsync(string capabilityId, bool paused, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            if (string.IsNullOrWhiteSpace(capabilityId))
            {
                throw new ArgumentException("capabilityIdを指定してください", nameof(capabilityId));
            }

            var normalizedCapabilityId = Uri.EscapeDataString(capabilityId.Trim());
            return SendOkAsync(
                new HttpMethod("PATCH"),
                $"/api/capabilities/{normalizedCapabilityId}/state",
                new { paused },
                cancellationToken);
        }

        public Task SendCapabilityResultAsync(OtomeKairoCapabilityResultRequest request, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            return SendOkAsync(HttpMethod.Post, "/api/capability/result", request, cancellationToken);
        }

        public Task<OtomeKairoCycleSummariesResponse> GetCycleSummariesAsync(int limit = 50, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            var normalizedLimit = limit > 0 ? limit : 50;
            return SendOtomeKairoAsync<OtomeKairoCycleSummariesResponse>(
                HttpMethod.Get,
                $"/api/inspection/cycle-summaries?limit={normalizedLimit}",
                null,
                cancellationToken);
        }

        public Task<OtomeKairoCurrentStateSnapshot> GetCurrentStateInspectionAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            return SendOtomeKairoAsync<OtomeKairoCurrentStateSnapshot>(
                HttpMethod.Get,
                "/api/inspection/current-state",
                null,
                cancellationToken);
        }

        public Task<OtomeKairoAutonomousRunsResponse> GetAutonomousRunsAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            return SendOtomeKairoAsync<OtomeKairoAutonomousRunsResponse>(
                HttpMethod.Get,
                "/api/autonomous-runs",
                null,
                cancellationToken);
        }

        public Task<OtomeKairoCycleTrace> GetCycleTraceAsync(string cycleId, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            if (string.IsNullOrWhiteSpace(cycleId))
            {
                throw new ArgumentException("cycleIdを指定してください", nameof(cycleId));
            }

            return SendOtomeKairoAsync<OtomeKairoCycleTrace>(
                HttpMethod.Get,
                $"/api/inspection/cycles/{cycleId.Trim()}",
                null,
                cancellationToken);
        }

        private async Task<T> SendOtomeKairoAsync<T>(
            HttpMethod method,
            string path,
            object? payload,
            CancellationToken cancellationToken,
            TimeSpan? requestTimeout = null)
        {
            var url = BuildUrl(path);
            using var request = new HttpRequestMessage(method, url);
            if (payload != null)
            {
                request.Content = CreateJsonContent(payload);
            }
            using var timeoutCts = new CancellationTokenSource(requestTimeout ?? DefaultRequestTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            try
            {
                using var response = await _httpClient.SendAsync(request, linkedCts.Token).ConfigureAwait(false);
                var responseBody = await response.Content.ReadAsStringAsync(linkedCts.Token).ConfigureAwait(false);

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
                if (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException("OtomeKairo APIリクエストがキャンセルされました", ex, cancellationToken);
                }
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

        private async Task SendOkAsync(HttpMethod method, string path, object? payload, CancellationToken cancellationToken)
        {
            var url = BuildUrl(path);
            using var request = new HttpRequestMessage(method, url);
            if (payload != null)
            {
                request.Content = CreateJsonContent(payload);
            }
            using var timeoutCts = new CancellationTokenSource(DefaultRequestTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            try
            {
                using var response = await _httpClient.SendAsync(request, linkedCts.Token).ConfigureAwait(false);
                var responseBody = await response.Content.ReadAsStringAsync(linkedCts.Token).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    var errorEnvelope = JsonSerializer.Deserialize<OtomeKairoErrorEnvelope>(responseBody, _serializerOptions);
                    var errorCode = errorEnvelope?.Error?.Code;
                    var errorMessage = errorEnvelope?.Error?.Message
                        ?? $"OtomeKairo APIエラー: {(int)response.StatusCode} {response.ReasonPhrase}";
                    throw new OtomeKairoApiException((int)response.StatusCode, errorCode, errorMessage);
                }

                var successEnvelope = JsonSerializer.Deserialize<OtomeKairoSuccessEnvelope<JsonElement>>(responseBody, _serializerOptions);
                if (successEnvelope == null || !successEnvelope.Ok)
                {
                    throw new InvalidOperationException("OtomeKairo APIレスポンスの解析に失敗しました");
                }
            }
            catch (TaskCanceledException ex)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException("OtomeKairo APIリクエストがキャンセルされました", ex, cancellationToken);
                }
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

        private StringContent CreateJsonContent(object payload)
        {
            var json = JsonSerializer.Serialize(payload, _serializerOptions);
            return new StringContent(json, Encoding.UTF8, "application/json");
        }

        private string BuildUrl(string path)
        {
            return $"{_baseUrl}/{path.TrimStart('/')}";
        }

        private static string BuildConfigResourcePath(string collectionPath, string resourceId)
        {
            // OtomeKairo の設定 ID は `memory_set:default` のように `:` を含み、
            // サーバーは最終 path segment をそのまま比較するためここでは URL エンコードしない。
            return $"{collectionPath}/{resourceId}";
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

        [JsonPropertyName("wake_policy")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Dictionary<string, object?>? WakePolicy { get; set; }
    }

    public class OtomeKairoConversationRequest
    {
        [JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;

        [JsonPropertyName("images")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? Images { get; set; }

        [JsonPropertyName("client_context")]
        public Dictionary<string, object?>? ClientContext { get; set; }
    }

    public class OtomeKairoConversationResponse
    {
        [JsonPropertyName("cycle_id")]
        public string CycleId { get; set; } = string.Empty;

        [JsonPropertyName("result_kind")]
        public string ResultKind { get; set; } = string.Empty;

        [JsonPropertyName("speech")]
        public OtomeKairoConversationSpeech? Speech { get; set; }

        [JsonPropertyName("capability_request")]
        public OtomeKairoCapabilityRequestSummary? CapabilityRequest { get; set; }
    }

    public class OtomeKairoConversationSpeech
    {
        [JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;
    }

    public class OtomeKairoCapabilityRequestSummary
    {
        [JsonPropertyName("request_id")]
        public string RequestId { get; set; } = string.Empty;

        [JsonPropertyName("capability_id")]
        public string CapabilityId { get; set; } = string.Empty;

        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("timeout_ms")]
        public int TimeoutMs { get; set; }
    }

    public class VisionClientContext
    {
        [JsonPropertyName("vision_source_id")]
        public string? VisionSourceId { get; set; }

        [JsonPropertyName("source_kind")]
        public string? SourceKind { get; set; }

        [JsonPropertyName("source_label")]
        public string? SourceLabel { get; set; }

        [JsonPropertyName("active_app")]
        public string? ActiveApp { get; set; }

        [JsonPropertyName("window_title")]
        public string? WindowTitle { get; set; }

        [JsonPropertyName("locale")]
        public string? Locale { get; set; }
    }

    public class VisionCaptureCapabilityResult
    {
        [JsonPropertyName("images")]
        public List<string> Images { get; set; } = new List<string>();

        [JsonPropertyName("client_context")]
        public VisionClientContext ClientContext { get; set; } = new VisionClientContext();

        [JsonPropertyName("error")]
        public string? Error { get; set; }
    }

    public class OtomeKairoCapabilityResultRequest
    {
        [JsonPropertyName("request_id")]
        public string RequestId { get; set; } = string.Empty;

        [JsonPropertyName("client_id")]
        public string ClientId { get; set; } = string.Empty;

        [JsonPropertyName("capability_id")]
        public string CapabilityId { get; set; } = string.Empty;

        [JsonPropertyName("result")]
        public VisionCaptureCapabilityResult Result { get; set; } = new VisionCaptureCapabilityResult();
    }
}
