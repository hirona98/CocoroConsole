using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace CocoroConsole.Services
{
    /// <summary>
    /// AmiVoice API（/v1/recognize）の同期（HTTP POST）クライアント。
    /// </summary>
    public class AmiVoiceSyncClient
    {
        private const string ENDPOINT = "https://acp-api.amivoice.com/v1/recognize";
        private const string DEFAULT_GRAMMAR_FILE_NAMES = "-a-general";
        private const float MIN_CONFIDENCE = 0.7f;
        private const float SINGLE_TOKEN_CONFIDENCE = 0.6f;
        private const int MIN_TOKENS = 2;
        private readonly string _apiKey;
        private readonly string _profileId;
        private static readonly HttpClient _httpClient;

        static AmiVoiceSyncClient()
        {
            // HttpClient設定
            var handler = new HttpClientHandler()
            {
                MaxConnectionsPerServer = 100,
                UseCookies = false
            };

            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30)
            };

            // Keep-Alive設定
            _httpClient.DefaultRequestHeaders.Connection.Add("keep-alive");
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "CocoroAI/5.2.0");
        }

        /// <summary>
        /// コンストラクタ。
        /// </summary>
        /// <param name="apiKey">AmiVoice APPKEY（recognize の u）</param>
        /// <param name="profileId">プロファイルID（推奨: 先頭に ":"。マイページ単語登録の場合は :{サービスID}）</param>
        public AmiVoiceSyncClient(string apiKey, string profileId = "")
        {
            // 入力値の正規化
            _apiKey = (apiKey ?? throw new ArgumentNullException(nameof(apiKey))).Trim();
            _profileId = NormalizeKeyValueValue(profileId, "profileId=");
        }

        /// <summary>
        /// "key=value" の入力を許容しつつ、value 部分だけに正規化します。
        /// </summary>
        /// <param name="raw">ユーザー入力</param>
        /// <param name="keyPrefix">key=（例: "profileId="）</param>
        /// <returns>正規化後のvalue（空文字あり）</returns>
        private static string NormalizeKeyValueValue(string raw, string keyPrefix)
        {
            // nullを空文字へ
            var value = (raw ?? string.Empty).Trim();

            // "key=value" 形式を許容
            if (!string.IsNullOrWhiteSpace(keyPrefix) &&
                value.StartsWith(keyPrefix, StringComparison.OrdinalIgnoreCase))
            {
                value = value.Substring(keyPrefix.Length).Trim();
            }

            // 先頭・末尾のダブルクォートを除去
            if (value.Length >= 2 && value.StartsWith("\"") && value.EndsWith("\""))
            {
                value = value.Substring(1, value.Length - 2).Trim();
            }

            return value;
        }

        /// <summary>
        /// 音声データ（WAV）をAmiVoiceに送信して文字起こし結果を返します。
        /// </summary>
        /// <param name="audioData">WAV音声データ</param>
        /// <returns>認識テキスト（失敗時は空文字）</returns>
        public async Task<string> RecognizeAsync(byte[] audioData)
        {
            // 入力チェック
            if (audioData == null || audioData.Length == 0)
                return string.Empty;

            try
            {
                // multipart/form-data を構築
                using var content = new MultipartFormDataContent();

                // u: APPKEY
                content.Add(new StringContent(_apiKey), "u");

                // d: 追加パラメータ（grammarFileNames / profileId）
                var dParam = $"grammarFileNames={DEFAULT_GRAMMAR_FILE_NAMES}";

                // profileId は ":" 付与をデフォルト（プロファイルがセッション終了時に上書きされる事故を防ぐ）
                if (!string.IsNullOrWhiteSpace(_profileId))
                {
                    var normalizedProfileId = _profileId.StartsWith(":") ? _profileId : $":{_profileId}";
                    dParam += $" profileId={normalizedProfileId}";

                    // 設定確認用（APPKEYはログに出さない）
                    System.Diagnostics.Debug.WriteLine($"[AmiVoice] grammarFileNames={DEFAULT_GRAMMAR_FILE_NAMES}, profileId={normalizedProfileId}");
                }

                content.Add(new StringContent(dParam), "d");

                // a: 音声データ
                using var audioContent = new ByteArrayContent(audioData);
                audioContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("audio/wav");
                content.Add(audioContent, "a", "audio.wav");

                // 送信
                var response = await _httpClient.PostAsync(ENDPOINT, content).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    // エラー応答の記録
                    var errorText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    System.Diagnostics.Debug.WriteLine($"AmiVoice API Error: {response.StatusCode} - {errorText}");
                    return string.Empty;
                }

                // JSONを取得・パース
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                var parseResult = JsonSerializer.Deserialize<AmiVoiceResult>(json);

                if (parseResult?.results == null || parseResult.results.Length == 0)
                    return string.Empty;

                // 先頭候補を採用
                var first = parseResult.results[0];
                var tokenCount = first.tokens?.Length ?? 0;

                // 信頼度フィルタ
                if (first.confidence < MIN_CONFIDENCE)
                {
                    System.Diagnostics.Debug.WriteLine($"AmiVoice Low confidence: {first.confidence:F2}");
                    return string.Empty;
                }

                // 1トークンの場合は厳しめに判定
                if (tokenCount < MIN_TOKENS && first.confidence < SINGLE_TOKEN_CONFIDENCE)
                {
                    System.Diagnostics.Debug.WriteLine($"AmiVoice Single token and low confidence: {first.confidence:F2}");
                    return string.Empty;
                }

                // 結果文字列を返却
                if (!string.IsNullOrWhiteSpace(first.text))
                    return first.text;

                return string.Empty;
            }
            catch (Exception ex)
            {
                // ネットワーク/パース等の例外を握りつぶして空文字を返す（リアルタイム処理のため）
                System.Diagnostics.Debug.WriteLine($"AmiVoice Recognition Error: {ex.Message}");
                return string.Empty;
            }
        }

        public void Dispose()
        {
            // 静的HttpClientは破棄しない（アプリケーション終了まで再利用）
        }
    }

    public class AmiVoiceResult
    {
        public AmiVoiceResultItem[]? results { get; set; }
        public string? code { get; set; }
        public string? message { get; set; }
    }

    public class AmiVoiceResultItem
    {
        public string text { get; set; } = string.Empty;
        public float confidence { get; set; }
        public AmiVoiceToken[]? tokens { get; set; }
    }

    public class AmiVoiceToken
    {
        public string written { get; set; } = string.Empty;
        public string spoken { get; set; } = string.Empty;
        public int starttime { get; set; }
        public int endtime { get; set; }
    }
}
