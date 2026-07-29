using NAudio.Wave;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CocoroConsole.Services
{
    /// <summary>
    /// CocoroShellが起動していない場合に、受信済みWAVをCocoroConsoleで再生する。
    /// </summary>
    public sealed class DirectAudioPlaybackService : IDisposable
    {
        private readonly SemaphoreSlim _playbackSemaphore = new SemaphoreSlim(1, 1);
        private bool _disposed;

        public async Task PlayAsync(byte[] wavBytes)
        {
            ArgumentNullException.ThrowIfNull(wavBytes);
            if (wavBytes.Length == 0)
            {
                throw new InvalidOperationException("再生するWAVが空です。");
            }

            await _playbackSemaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                using var audioStream = new MemoryStream(wavBytes, writable: false);
                using var reader = new WaveFileReader(audioStream);
                using var output = new WaveOutEvent();
                var completion = new TaskCompletionSource<object?>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
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
                Debug.WriteLine($"直接WAV再生エラー: {ex.Message}");
                throw;
            }
            finally
            {
                _playbackSemaphore.Release();
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _playbackSemaphore.Dispose();
        }
    }
}
