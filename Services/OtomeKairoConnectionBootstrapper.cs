using System;
using System.Threading;
using System.Threading.Tasks;

namespace CocoroConsole.Services
{
    /// <summary>
    /// 通常機能を開始する前に確定したOtomeKairo接続情報。
    /// </summary>
    internal sealed class OtomeKairoConnectionResult
    {
        public OtomeKairoConnectionResult(string serverUrl, string consoleAccessToken)
        {
            ServerUrl = serverUrl;
            ConsoleAccessToken = consoleAccessToken;
        }

        public string ServerUrl { get; }
        public string ConsoleAccessToken { get; }
    }

    /// <summary>
    /// OtomeKairoのbootstrap面を使い、接続先と認証状態を通常機能の開始前に検証する。
    /// </summary>
    internal sealed class OtomeKairoConnectionBootstrapper
    {
        public const string RequiredApiVersion = "0.10.0";
        private static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(5);

        public async Task<OtomeKairoConnectionResult> ConnectAsync(
            string serverUrl,
            string? currentServerUrl,
            string? currentAccessToken,
            CancellationToken cancellationToken = default)
        {
            var normalizedServerUrl = NormalizeServerUrl(serverUrl);
            var normalizedCurrentServerUrl = TryNormalizeServerUrl(currentServerUrl);
            var isCurrentServer = string.Equals(
                normalizedServerUrl,
                normalizedCurrentServerUrl,
                StringComparison.OrdinalIgnoreCase);
            var accessToken = isCurrentServer
                ? (currentAccessToken ?? string.Empty).Trim()
                : string.Empty;

            using var timeoutCts = new CancellationTokenSource(ConnectionTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeoutCts.Token);
            using var apiClient = new OtomeKairoApiClient(normalizedServerUrl, accessToken);

            try
            {
                var identity = await apiClient
                    .GetServerIdentityAsync(linkedCts.Token)
                    .ConfigureAwait(false);
                if (!string.Equals(
                        identity.ApiVersion,
                        RequiredApiVersion,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"接続先 OtomeKairo API は {identity.ApiVersion} です。本ソフトは {RequiredApiVersion} しか対応していません。");
                }

                if (!string.IsNullOrWhiteSpace(accessToken))
                {
                    try
                    {
                        await apiClient
                            .GetOtomeKairoStatusAsync(linkedCts.Token)
                            .ConfigureAwait(false);
                        return new OtomeKairoConnectionResult(normalizedServerUrl, accessToken);
                    }
                    catch (OtomeKairoApiException ex) when (
                        ex.ErrorCode == "invalid_token" ||
                        ex.ErrorCode == "bootstrap_required")
                    {
                        // 保存済み資格が接続先で無効な場合はbootstrap面から取得し直す。
                    }
                }

                var acquired = await apiClient
                    .AcquireConsoleAccessTokenAsync(linkedCts.Token)
                    .ConfigureAwait(false);
                accessToken = acquired.ConsoleAccessToken.Trim();
                if (string.IsNullOrWhiteSpace(accessToken))
                {
                    throw new InvalidOperationException(
                        "OtomeKairoからconsole_access_tokenを取得できませんでした。");
                }

                apiClient.SetBearerToken(accessToken);
                await apiClient
                    .GetOtomeKairoStatusAsync(linkedCts.Token)
                    .ConfigureAwait(false);
                return new OtomeKairoConnectionResult(normalizedServerUrl, accessToken);
            }
            catch (OperationCanceledException ex) when (
                timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException(
                    "OtomeKairoへの接続が5秒以内に完了しませんでした。",
                    ex);
            }
        }

        public static string NormalizeServerUrl(string? serverUrl)
        {
            var normalized = (serverUrl ?? string.Empty).Trim().TrimEnd('/');
            if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri)
                || uri.Scheme != Uri.UriSchemeHttps
                || string.IsNullOrWhiteSpace(uri.Host)
                || !string.IsNullOrEmpty(uri.UserInfo)
                || (uri.AbsolutePath != "/" && !string.IsNullOrEmpty(uri.AbsolutePath))
                || !string.IsNullOrEmpty(uri.Query)
                || !string.IsNullOrEmpty(uri.Fragment))
            {
                throw new InvalidOperationException(
                    "OtomeKairoのサーバーURLには https://<host>:<port> の形式を指定してください。");
            }

            return uri.GetLeftPart(UriPartial.Authority);
        }

        private static string? TryNormalizeServerUrl(string? serverUrl)
        {
            try
            {
                return NormalizeServerUrl(serverUrl);
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }
    }
}
