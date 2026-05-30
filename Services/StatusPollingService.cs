using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;

namespace CocoroConsole.Services
{
    /// <summary>
    /// OtomeKairoのステータス状態を表す列挙型
    /// </summary>
    public enum OtomeKairoStatus
    {
        /// <summary>OtomeKairo起動待ち</summary>
        WaitingForStartup,
        /// <summary>正常動作中（OtomeKairoとのポーリングが正常なとき）</summary>
        Normal,
        /// <summary>LLMメッセージ処理中</summary>
        ProcessingMessage,
        /// <summary>LLM画像処理中</summary>
        ProcessingImage
    }


    /// <summary>
    /// OtomeKairoのステータスポーリングサービス
    /// </summary>
    public class StatusPollingService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _probeEndpoint;
        private readonly Timer _pollingTimer;
        private int _pollingInProgress = 0;
        private OtomeKairoStatus _currentStatus = OtomeKairoStatus.WaitingForStartup;
        private volatile bool _disposed = false;

        /// <summary>
        /// ステータス変更時のイベント
        /// </summary>
        public event EventHandler<OtomeKairoStatus>? StatusChanged;

        /// <summary>
        /// 現在のステータス
        /// </summary>
        public OtomeKairoStatus CurrentStatus => _currentStatus;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="baseUrl">OtomeKairoのベースURL（デフォルト: https://127.0.0.1:55601）</param>
        public StatusPollingService(string baseUrl = "https://127.0.0.1:55601")
        {
            // --- OtomeKairo は自己署名HTTPSを前提とする ---
            // LAN公開（Web UI含む）に寄せるため HTTPS 必須の設計になっている。
            // CocoroConsole はローカル接続のみの前提で、証明書のホスト検証は行わない。
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true
            };
            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromMilliseconds(800)
            };
            _probeEndpoint = $"{baseUrl.TrimEnd('/')}/api/bootstrap/probe";

            // 1秒間隔でポーリング開始（起動待ち用）
            _pollingTimer = new Timer(_ =>
            {
                _ = PollHealthStatusAsync().ContinueWith(
                    task =>
                    {
                        if (task.Exception != null)
                        {
                            Debug.WriteLine($"[StatusPolling] PollHealthStatus failed: {task.Exception.GetBaseException().Message}");
                        }
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted,
                    TaskScheduler.Default);
            }, null, TimeSpan.Zero, TimeSpan.FromSeconds(1));
        }

        /// <summary>
        /// ヘルスチェックを実行してステータスを更新（同期・ブロッキング）
        /// </summary>
        private async Task PollHealthStatusAsync()
        {
            if (_disposed) return;
            if (Interlocked.Exchange(ref _pollingInProgress, 1) == 1)
            {
                return;
            }

            try
            {
                using var response = await _httpClient.GetAsync(_probeEndpoint).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    using var document = JsonDocument.Parse(content);
                    var probeSucceeded = document.RootElement.TryGetProperty("ok", out var okElement)
                        && okElement.ValueKind == JsonValueKind.True;

                    if (probeSucceeded)
                    {
                        if (_currentStatus == OtomeKairoStatus.WaitingForStartup)
                        {
                            UpdateStatus(OtomeKairoStatus.Normal);
                            // 起動完了したら10秒間隔に変更
                            _pollingTimer.Change(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
                        }
                    }
                    else
                    {
                        if (_currentStatus != OtomeKairoStatus.WaitingForStartup)
                        {
                            UpdateStatus(OtomeKairoStatus.WaitingForStartup);
                            // 起動待ちに戻ったら1秒間隔に変更
                            _pollingTimer.Change(TimeSpan.Zero, TimeSpan.FromSeconds(1));
                        }
                    }
                }
                else
                {
                    if (_currentStatus != OtomeKairoStatus.WaitingForStartup)
                    {
                        UpdateStatus(OtomeKairoStatus.WaitingForStartup);
                        _pollingTimer.Change(TimeSpan.Zero, TimeSpan.FromSeconds(1));
                    }
                }
            }
            catch (Exception)
            {
                if (_currentStatus != OtomeKairoStatus.WaitingForStartup)
                {
                    UpdateStatus(OtomeKairoStatus.WaitingForStartup);
                    _pollingTimer.Change(TimeSpan.Zero, TimeSpan.FromSeconds(1));
                }
            }
            finally
            {
                Interlocked.Exchange(ref _pollingInProgress, 0);
            }
        }

        /// <summary>
        /// ステータスを更新してイベントを発火
        /// </summary>
        /// <param name="newStatus">新しいステータス</param>
        private void UpdateStatus(OtomeKairoStatus newStatus)
        {
            if (_currentStatus != newStatus)
            {
                _currentStatus = newStatus;
                StatusChanged?.Invoke(this, newStatus);
            }
        }

        /// <summary>
        /// 処理状態を手動で設定（通信開始時に呼び出し）
        /// </summary>
        /// <param name="processingStatus">処理状態</param>
        public void SetProcessingStatus(OtomeKairoStatus processingStatus)
        {
            if (processingStatus == OtomeKairoStatus.ProcessingMessage ||
                processingStatus == OtomeKairoStatus.ProcessingImage)
            {
                UpdateStatus(processingStatus);
            }
        }

        /// <summary>
        /// 処理完了時に正常状態に戻す
        /// </summary>
        public void SetNormalStatus()
        {
            UpdateStatus(OtomeKairoStatus.Normal);
        }

        /// <summary>
        /// 再起動開始を明示して起動待ち状態に戻す
        /// </summary>
        public void SetWaitingForStartup()
        {
            // 起動待ち状態を明示し、ポーリング間隔を短くする
            UpdateStatus(OtomeKairoStatus.WaitingForStartup);
            _pollingTimer.Change(TimeSpan.Zero, TimeSpan.FromSeconds(1));
        }

        /// <summary>
        /// リソースを解放
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;

            _pollingTimer?.Dispose();
            _httpClient?.Dispose();
        }
    }
}
