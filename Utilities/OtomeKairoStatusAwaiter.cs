using CocoroAI.Services;
using CocoroConsole.Services;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace CocoroConsole.Utilities
{
    /// <summary>
    /// OtomeKairo の起動完了状態を待機するヘルパー。
    /// </summary>
    public static class OtomeKairoStatusAwaiter
    {
        public static bool IsReadyStatus(OtomeKairoStatus status)
        {
            return status == OtomeKairoStatus.Normal ||
                   status == OtomeKairoStatus.ProcessingMessage ||
                   status == OtomeKairoStatus.ProcessingImage;
        }

        public static async Task WaitUntilReadyAsync(
            ICommunicationService communicationService,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(communicationService);

            if (IsReadyStatus(communicationService.CurrentStatus))
            {
                return;
            }

            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            void OnStatusChanged(object? sender, OtomeKairoStatus status)
            {
                if (IsReadyStatus(status))
                {
                    tcs.TrySetResult(true);
                }
            }

            using var registration = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
            communicationService.StatusChanged += OnStatusChanged;

            try
            {
                if (IsReadyStatus(communicationService.CurrentStatus))
                {
                    return;
                }

                var timeoutTask = Task.Delay(timeout);
                var completedTask = await Task.WhenAny(tcs.Task, timeoutTask).ConfigureAwait(false);
                if (completedTask != tcs.Task)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    throw new TimeoutException("OtomeKairoの起動待機がタイムアウトしました。");
                }

                await tcs.Task.ConfigureAwait(false);
            }
            finally
            {
                communicationService.StatusChanged -= OnStatusChanged;
            }
        }
    }
}
