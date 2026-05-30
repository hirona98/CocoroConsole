using CocoroConsole.Communication;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace CocoroConsole.Services
{
    /// <summary>
    /// CocoroShellを経由できない場合に、CocoroConsoleから直接TTSを実行するサービス。
    /// </summary>
    public sealed class DirectTtsService : IDisposable
    {
        private static readonly Regex SpeechTagRegex = new Regex(@"\[[A-Za-z_][A-Za-z0-9_\-]*:[^\]]*\]", RegexOptions.Compiled);
        private readonly HttpClient _httpClient;
        private readonly SemaphoreSlim _playbackSemaphore = new SemaphoreSlim(1, 1);
        private bool _disposed;

        public DirectTtsService()
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(60)
            };
        }

        public async Task SpeakAsync(string content, CharacterSettings? character)
        {
            if (string.IsNullOrWhiteSpace(content) || character == null || !character.isUseTTS)
            {
                return;
            }

            var text = NormalizeTextForSpeech(content);
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            byte[] audioBytes = character.ttsType switch
            {
                "style-bert-vits2" => await SynthesizeStyleBertVits2Async(text, character.styleBertVits2Config).ConfigureAwait(false),
                "aivis-cloud" => await SynthesizeAivisCloudAsync(text, character.aivisCloudConfig).ConfigureAwait(false),
                _ => await SynthesizeVoicevoxAsync(text, character.voicevoxConfig).ConfigureAwait(false),
            };

            await PlayAudioAsync(audioBytes, GetAudioFormat(character)).ConfigureAwait(false);
        }

        private static string NormalizeTextForSpeech(string content)
        {
            var text = SpeechTagRegex.Replace(content, string.Empty);
            text = Regex.Replace(text, @"<thinking>.*?</thinking>", string.Empty, RegexOptions.Singleline | RegexOptions.IgnoreCase);
            return text.Replace(" ", string.Empty).Replace("\n", string.Empty).Trim();
        }

        private async Task<byte[]> SynthesizeVoicevoxAsync(string text, VoicevoxConfig config)
        {
            if (string.IsNullOrWhiteSpace(config.endpointUrl))
            {
                throw new InvalidOperationException("VOICEVOXのエンドポイントURLが設定されていません");
            }

            var endpoint = config.endpointUrl.TrimEnd('/');
            var queryUrl = $"{endpoint}/audio_query?speaker={config.speakerId}&text={Uri.EscapeDataString(text)}";
            using var queryResponse = await _httpClient.PostAsync(queryUrl, null).ConfigureAwait(false);
            queryResponse.EnsureSuccessStatusCode();

            var queryJson = await queryResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
            var query = JsonNode.Parse(queryJson)?.AsObject()
                ?? throw new InvalidOperationException("VOICEVOXのaudio_queryレスポンスを解析できませんでした");

            SetJsonValueIfPresent(query, "speedScale", config.speedScale);
            SetJsonValueIfPresent(query, "pitchScale", config.pitchScale);
            SetJsonValueIfPresent(query, "intonationScale", config.intonationScale);
            SetJsonValueIfPresent(query, "volumeScale", config.volumeScale);
            SetJsonValueIfPresent(query, "prePhonemeLength", config.prePhonemeLength);
            SetJsonValueIfPresent(query, "postPhonemeLength", config.postPhonemeLength);
            SetJsonValueIfPresent(query, "outputSamplingRate", config.outputSamplingRate);
            SetJsonValueIfPresent(query, "outputStereo", config.outputStereo);

            var synthesisUrl = $"{endpoint}/synthesis?speaker={config.speakerId}";
            using var content = new StringContent(query.ToJsonString(), Encoding.UTF8, "application/json");
            using var synthesisResponse = await _httpClient.PostAsync(synthesisUrl, content).ConfigureAwait(false);
            synthesisResponse.EnsureSuccessStatusCode();
            return await synthesisResponse.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        }

        private async Task<byte[]> SynthesizeStyleBertVits2Async(string text, StyleBertVits2Config config)
        {
            if (string.IsNullOrWhiteSpace(config.endpointUrl))
            {
                throw new InvalidOperationException("Style-Bert-VITS2のエンドポイントURLが設定されていません");
            }

            var endpoint = config.endpointUrl.TrimEnd('/');
            var query = new List<string>
            {
                $"text={Uri.EscapeDataString(text)}",
                $"model_id={config.modelId}",
                $"sdp_ratio={config.sdpRatio.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                $"noise={config.noise.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                $"noisew={config.noiseW.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                $"length={config.length.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                $"language={Uri.EscapeDataString(config.language)}",
                $"auto_split={config.autoSplit.ToString().ToLowerInvariant()}",
                $"split_interval={config.splitInterval.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                $"style={Uri.EscapeDataString(config.style)}",
                $"style_weight={config.styleWeight.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
            };

            if (!string.IsNullOrWhiteSpace(config.modelName)) query.Add($"model_name={Uri.EscapeDataString(config.modelName)}");
            if (!string.IsNullOrWhiteSpace(config.speakerName)) query.Add($"speaker_name={Uri.EscapeDataString(config.speakerName)}");
            if (!string.IsNullOrWhiteSpace(config.assistText))
            {
                query.Add($"assist_text={Uri.EscapeDataString(config.assistText)}");
                query.Add($"assist_text_weight={config.assistTextWeight.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            }
            if (!string.IsNullOrWhiteSpace(config.referenceAudioPath)) query.Add($"reference_audio_path={Uri.EscapeDataString(config.referenceAudioPath)}");

            using var response = await _httpClient.GetAsync($"{endpoint}/voice?{string.Join("&", query)}").ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        }

        private async Task<byte[]> SynthesizeAivisCloudAsync(string text, AivisCloudConfig config)
        {
            if (string.IsNullOrWhiteSpace(config.apiKey))
            {
                throw new InvalidOperationException("Aivis CloudのAPIキーが設定されていません");
            }

            var url = string.IsNullOrWhiteSpace(config.endpointUrl)
                ? "https://api.aivis-project.com/v1/tts/synthesize"
                : config.endpointUrl;

            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", config.apiKey);

            var payload = new Dictionary<string, object?>
            {
                ["text"] = text,
                ["model_uuid"] = config.modelUuid,
                ["style_id"] = config.styleId,
                ["use_ssml"] = config.useSSML,
                ["language"] = config.language,
                ["speaking_rate"] = config.speakingRate,
                ["emotional_intensity"] = config.emotionalIntensity,
                ["tempo_dynamics"] = config.tempoDynamics,
                ["pitch"] = config.pitch,
                ["volume"] = config.volume,
                ["output_format"] = string.IsNullOrWhiteSpace(config.outputFormat) ? "wav" : config.outputFormat,
                ["output_sampling_rate"] = config.outputSamplingRate,
                ["output_audio_channels"] = config.outputAudioChannels
            };

            if (!string.IsNullOrWhiteSpace(config.speakerUuid)) payload["speaker_uuid"] = config.speakerUuid;
            if (!string.IsNullOrWhiteSpace(config.styleName)) payload["style_name"] = config.styleName;
            if (config.outputBitrate > 0) payload["output_bitrate"] = config.outputBitrate;

            request.Content = JsonContent.Create(payload);
            using var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        }

        private static void SetJsonValueIfPresent(JsonObject query, string key, object value)
        {
            if (query.ContainsKey(key))
            {
                query[key] = JsonValue.Create(value);
            }
        }

        private static string GetAudioFormat(CharacterSettings character)
        {
            if (character.ttsType == "aivis-cloud" && !string.IsNullOrWhiteSpace(character.aivisCloudConfig.outputFormat))
            {
                return character.aivisCloudConfig.outputFormat;
            }

            return "wav";
        }

        private async Task PlayAudioAsync(byte[] audioBytes, string format)
        {
            if (audioBytes.Length == 0)
            {
                return;
            }

            await _playbackSemaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                using var audioStream = new MemoryStream(audioBytes);
                using var reader = CreateReader(audioStream, format);
                using var output = new WaveOutEvent();
                var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
                output.PlaybackStopped += (_, e) =>
                {
                    if (e.Exception != null)
                    {
                        completion.TrySetException(e.Exception);
                    }
                    else
                    {
                        completion.TrySetResult(null);
                    }
                };

                output.Init(reader);
                output.Play();
                await completion.Task.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"直接TTS再生エラー: {ex.Message}");
                throw;
            }
            finally
            {
                _playbackSemaphore.Release();
            }
        }

        private static WaveStream CreateReader(Stream audioStream, string format)
        {
            if (string.Equals(format, "mp3", StringComparison.OrdinalIgnoreCase))
            {
                return new Mp3FileReader(audioStream);
            }

            return new WaveFileReader(audioStream);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _httpClient.Dispose();
            _playbackSemaphore.Dispose();
        }
    }
}
