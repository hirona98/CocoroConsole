using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace CocoroConsole.Communication
{
    /// <summary>
    /// otomekairo の /api/logs/stream に接続してログを受信するクライアント
    /// </summary>
    public class LogStreamClient : ResilientWebSocketClientBase
    {
        public event EventHandler<IReadOnlyList<LogMessage>>? LogsReceived;
        public event EventHandler<bool>? ConnectionStateChanged;
        public event EventHandler<string>? ErrorOccurred;

        public LogStreamClient(Uri webSocketUri, string bearerToken)
            : base(webSocketUri, bearerToken)
        {
        }

        protected override string ConnectFailureLabel => "ログストリーム接続失敗";
        protected override string ReceiveFailureLabel => "ログストリーム受信エラー";
        protected override string AuthenticationFailureMessage => "ログストリーム認証エラーのため再接続を停止しました。";

        protected override void RaiseConnectionStateChanged(bool isConnected)
        {
            ConnectionStateChanged?.Invoke(this, isConnected);
        }

        protected override void RaiseError(string message)
        {
            ErrorOccurred?.Invoke(this, message);
        }

        protected override void HandleTextMessage(string json)
        {
            HandleMessage(json);
        }

        private void HandleMessage(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var logs = new List<LogMessage>();

                if (root.ValueKind == JsonValueKind.Array)
                {
                    foreach (var element in root.EnumerateArray())
                    {
                        if (TryParseLog(element, out var log))
                        {
                            logs.Add(log);
                        }
                    }
                }
                else if (root.ValueKind == JsonValueKind.Object && TryParseLog(root, out var logMessage))
                {
                    logs.Add(logMessage);
                }

                if (logs.Count > 0)
                {
                    LogsReceived?.Invoke(this, logs);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LogStream] JSON parse error: {ex.Message}");
                ErrorOccurred?.Invoke(this, $"ログパースエラー: {ex.Message}");
            }
        }

        private bool TryParseLog(JsonElement element, out LogMessage logMessage)
        {
            logMessage = new LogMessage();
            try
            {
                DateTime timestamp = DateTime.Now;
                if (element.TryGetProperty("ts", out var tsElement))
                {
                    var tsString = tsElement.GetString();
                    if (!string.IsNullOrEmpty(tsString) &&
                        DateTime.TryParse(tsString, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed))
                    {
                        timestamp = parsed.ToLocalTime();
                    }
                }

                var level = element.TryGetProperty("level", out var levelElement) ? levelElement.GetString() : "INFO";
                var logger = element.TryGetProperty("logger", out var loggerElement) ? loggerElement.GetString() : string.Empty;
                var message = element.TryGetProperty("msg", out var msgElement) ? msgElement.GetString() : string.Empty;
                message = CompactJsonWhitespaceInLog(message ?? string.Empty);

                logMessage = new LogMessage
                {
                    timestamp = timestamp,
                    level = NormalizeLogLevel(level),
                    component = logger ?? string.Empty,
                    message = message ?? string.Empty
                };
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string NormalizeLogLevel(string? level)
        {
            var normalized = (level ?? "INFO").Trim().ToUpperInvariant();
            return normalized switch
            {
                "DEBUG" => "DEBUG",
                "INFO" => "INFO",
                "WARN" => "WARNING",
                "WARNING" => "WARNING",
                "ERROR" => "ERROR",
                _ => "INFO"
            };
        }

        private static string CompactJsonWhitespaceInLog(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return message;
            }

            var markerIndex = FindFirstMarkerIndex(message);
            if (markerIndex < 0)
            {
                return message;
            }

            var jsonStart = message.IndexOfAny(new[] { '{', '[' }, markerIndex);
            if (jsonStart < 0)
            {
                return message;
            }

            var prefix = message.Substring(0, jsonStart);
            var jsonPart = message.Substring(jsonStart);
            var compacted = CollapseWhitespaceOutsideStrings(jsonPart);
            return prefix + compacted;
        }

        private static int FindFirstMarkerIndex(string message)
        {
            string[] markers =
            {
                "LLM response (json)",
                "LLM request (json)",
                "LLM response (chat)",
                "LLM request (chat)",
                "LLM response (vision)",
                "LLM request (vision)",
            };

            var bestIndex = -1;
            foreach (var marker in markers)
            {
                var index = message.IndexOf(marker, StringComparison.Ordinal);
                if (index < 0)
                {
                    continue;
                }

                if (bestIndex < 0 || index < bestIndex)
                {
                    bestIndex = index;
                }
            }

            return bestIndex;
        }

        private static string CollapseWhitespaceOutsideStrings(string text)
        {
            var sb = new StringBuilder(text.Length);
            bool inString = false;
            bool escaped = false;
            bool inWhitespace = false;

            foreach (var ch in text)
            {
                if (inString)
                {
                    sb.Append(ch);
                    if (escaped)
                    {
                        escaped = false;
                    }
                    else if (ch == '\\')
                    {
                        escaped = true;
                    }
                    else if (ch == '"')
                    {
                        inString = false;
                    }
                    continue;
                }

                if (ch == '"')
                {
                    inString = true;
                    inWhitespace = false;
                    sb.Append(ch);
                    continue;
                }

                if (char.IsWhiteSpace(ch))
                {
                    if (!inWhitespace)
                    {
                        sb.Append(' ');
                        inWhitespace = true;
                    }
                    continue;
                }

                inWhitespace = false;
                sb.Append(ch);
            }

            return sb.ToString();
        }

    }
}
