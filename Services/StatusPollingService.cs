using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;

namespace CocoroConsole.Services
{
    /// <summary>
    /// CocoroGhostのステータス状態を表す列挙型
    /// </summary>
    public enum CocoroGhostStatus
    {
        /// <summary>CocoroGhost起動待ち</summary>
        WaitingForStartup,
        /// <summary>正常動作中（CocoroGhostとのポーリングが正常なとき）</summary>
        Normal,
        /// <summary>LLMメッセージ処理中</summary>
        ProcessingMessage,
        /// <summary>LLM画像処理中</summary>
        ProcessingImage
    }


    /// <summary>
    /// CocoroGhostのステータスポーリングサービス
    /// </summary>
    public class StatusPollingService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _healthEndpoint;
        private readonly Timer _pollingTimer;
        private int _pollingInProgress = 0;
        private CocoroGhostStatus _currentStatus = CocoroGhostStatus.WaitingForStartup;
        private volatile bool _disposed = false;

        /// <summary>
        /// ステータス変更時のイベント
        /// </summary>
        public event EventHandler<CocoroGhostStatus>? StatusChanged;

        /// <summary>
        /// 現在のステータス
        /// </summary>
        public CocoroGhostStatus CurrentStatus => _currentStatus;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="baseUrl">CocoroGhostのベースURL（デフォルト: http://127.0.0.1:55601）</param>
        public StatusPollingService(string baseUrl = "http://127.0.0.1:55601")
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromMilliseconds(800)
            };
            _healthEndpoint = $"{baseUrl.TrimEnd('/')}/api/health";

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
                using var response = await _httpClient.GetAsync(_healthEndpoint).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    var healthCheck = JsonSerializer.Deserialize<Communication.HealthCheckResponse>(content);

                    if (healthCheck != null && healthCheck.status == "healthy")
                    {
                        if (_currentStatus == CocoroGhostStatus.WaitingForStartup)
                        {
                            UpdateStatus(CocoroGhostStatus.Normal);
                            // 起動完了したら10秒間隔に変更
                            _pollingTimer.Change(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
                        }
                    }
                    else
                    {
                        if (_currentStatus != CocoroGhostStatus.WaitingForStartup)
                        {
                            UpdateStatus(CocoroGhostStatus.WaitingForStartup);
                            // 起動待ちに戻ったら1秒間隔に変更
                            _pollingTimer.Change(TimeSpan.Zero, TimeSpan.FromSeconds(1));
                        }
                    }
                }
                else
                {
                    if (_currentStatus != CocoroGhostStatus.WaitingForStartup)
                    {
                        UpdateStatus(CocoroGhostStatus.WaitingForStartup);
                        _pollingTimer.Change(TimeSpan.Zero, TimeSpan.FromSeconds(1));
                    }
                }
            }
            catch (Exception)
            {
                if (_currentStatus != CocoroGhostStatus.WaitingForStartup)
                {
                    UpdateStatus(CocoroGhostStatus.WaitingForStartup);
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
        private void UpdateStatus(CocoroGhostStatus newStatus)
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
        public void SetProcessingStatus(CocoroGhostStatus processingStatus)
        {
            if (processingStatus == CocoroGhostStatus.ProcessingMessage ||
                processingStatus == CocoroGhostStatus.ProcessingImage)
            {
                UpdateStatus(processingStatus);
            }
        }

        /// <summary>
        /// 処理完了時に正常状態に戻す
        /// </summary>
        public void SetNormalStatus()
        {
            UpdateStatus(CocoroGhostStatus.Normal);
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
