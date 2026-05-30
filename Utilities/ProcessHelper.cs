using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using CocoroConsole.Services;

namespace CocoroConsole.Utilities
{
    /// <summary>
    /// プロセス操作の種類を定義する列挙型
    /// </summary>
    public enum ProcessOperation
    {
        /// <summary>既存のプロセスを終了して新しいプロセスを起動</summary>
        RestartIfRunning,
        /// <summary>プロセスを強制終了</summary>
        Terminate,
        /// <summary>プロセスの存在チェックのみ</summary>
        CheckOnly
    }

    /// <summary>
    /// プロセス管理に関するユーティリティメソッドを提供するヘルパークラス
    /// </summary>
    public static class ProcessHelper
    {
        private static readonly HttpClient httpClient = new HttpClient()
        {
            Timeout = TimeSpan.FromSeconds(5)
        };
        /// <summary>
        /// 指定した名前のプロセスに対して操作を行います
        /// </summary>
        /// <param name="processName">プロセス名（拡張子なし）</param>
        /// <param name="operation">実行する操作</param>
        /// <returns>プロセスが存在する場合はtrue、存在しない場合はfalse</returns>
        public static bool ExitProcess(string processName, ProcessOperation operation)
        {
            return ExitProcessAsync(processName, operation).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 指定した名前のプロセスに対して操作を行います（非同期版）
        /// </summary>
        /// <param name="processName">プロセス名（拡張子なし）</param>
        /// <param name="operation">実行する操作</param>
        /// <returns>プロセスが存在する場合はtrue、存在しない場合はfalse</returns>
        public static async Task<bool> ExitProcessAsync(string processName, ProcessOperation operation)
        {
            // --- CheckOnly は存在確認のみ（終了要求は送らない） ---
            if (operation == ProcessOperation.CheckOnly)
            {
                return Process.GetProcessesByName(processName).Length > 0;
            }

            // --- まずは REST API による協調的終了を試みる ---
            bool gracefullyTerminated = await TryGracefulTerminationAsync(processName).ConfigureAwait(false);

            // --- 協調的終了を送れた場合は、プロセスの完全終了を待機 ---
            if (gracefullyTerminated)
            {
                Debug.WriteLine($"{processName} に終了要求を送信しました。終了を待機中...");
                bool terminated = await WaitForProcessTerminationAsync(processName).ConfigureAwait(false);
                if (terminated)
                {
                    Debug.WriteLine($"{processName} の終了を確認しました。");
                }
                else
                {
                    Debug.WriteLine($"{processName} の終了待機がタイムアウトしました。");
                }
                return terminated;
            }

            // --- 強制終了（Terminate / RestartIfRunning）は OS の Kill を実行 ---
            if (operation == ProcessOperation.Terminate || operation == ProcessOperation.RestartIfRunning)
            {
                bool forced = await TryForceTerminationAsync(processName).ConfigureAwait(false);
                if (forced)
                {
                    Debug.WriteLine($"{processName} を強制終了しました。終了を待機中...");
                    return await WaitForProcessTerminationAsync(processName).ConfigureAwait(false);
                }
            }

            return false;
        }

        /// <summary>
        /// プロセスが完全に終了するまで待機します（非同期版）
        /// </summary>
        /// <param name="processName">プロセス名</param>
        /// <param name="timeoutSeconds">タイムアウト秒数</param>
        /// <returns>プロセスが終了した場合はtrue、タイムアウトした場合はfalse</returns>
        private static async Task<bool> WaitForProcessTerminationAsync(string processName, int timeoutSeconds = 30)
        {
            var stopwatch = Stopwatch.StartNew();
            var timeout = TimeSpan.FromSeconds(timeoutSeconds);

            // 設定から各プロセスのポート番号を取得
            var settings = AppSettings.Instance;
            int? port = processName.ToLower() switch
            {
                "otomekairo" => settings.OtomeKairoPort,
                "cocoroshell" => settings.CocoroShellPort,
                _ => null
            };

            // ポートが分かる場合は、まずポートベースでプロセスIDを取得
            int? processId = null;
            if (port.HasValue)
            {
                processId = GetProcessIdByPort(port.Value);
                if (processId.HasValue)
                {
                    Debug.WriteLine($"{processName} のプロセスID: {processId}");
                }
            }

            // プロセスIDが取得できた場合は、そのプロセスの終了を監視
            if (processId.HasValue)
            {
                while (stopwatch.Elapsed < timeout)
                {
                    if (!IsProcessRunning(processId.Value))
                    {
                        return true;
                    }
                    await Task.Delay(100).ConfigureAwait(false);
                }
            }
            else
            {
                // プロセスIDが取得できない場合は、プロセス名で監視
                while (stopwatch.Elapsed < timeout)
                {
                    var processes = Process.GetProcessesByName(processName);
                    if (processes.Length == 0)
                    {
                        return true;
                    }
                    foreach (var process in processes)
                    {
                        process.Dispose();
                    }
                    await Task.Delay(100).ConfigureAwait(false);
                }
            }

            return false;
        }

        /// <summary>
        /// 指定されたポート番号を使用しているプロセスのIDを取得します
        /// </summary>
        /// <param name="port">ポート番号</param>
        /// <returns>プロセスID（見つからない場合はnull）</returns>
        private static int? GetProcessIdByPort(int port)
        {
            try
            {
                var processInfo = new ProcessStartInfo("netstat", "-ano")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(processInfo);
                if (process == null) return null;

                using var reader = process.StandardOutput;
                string? line;

                while ((line = reader.ReadLine()) != null)
                {
                    // ポート番号を含む行でLISTENING状態のものを探す
                    if (line.Contains($":{port} ") && line.Contains("LISTENING"))
                    {
                        // 行の最後の数字（PID）を抽出
                        var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length > 0 && int.TryParse(parts[^1], out int pid))
                        {
                            return pid;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"プロセスID取得中にエラーが発生しました: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// 指定されたプロセスIDのプロセスが実行中かどうかを確認します
        /// </summary>
        /// <param name="processId">プロセスID</param>
        /// <returns>実行中の場合true、終了している場合false</returns>
        private static bool IsProcessRunning(int processId)
        {
            try
            {
                Process.GetProcessById(processId);
                return true;
            }
            catch (ArgumentException)
            {
                // プロセスが存在しない場合
                return false;
            }
        }

        /// <summary>
        /// REST APIを使用してプロセスに協調的終了を要求します
        /// </summary>
        /// <param name="processName">プロセス名</param>
        /// <returns>終了シグナルの送信に成功した場合はtrue</returns>
        private static async Task<bool> TryGracefulTerminationAsync(string processName)
        {
            try
            {
                if (processName.Equals("OtomeKairo", StringComparison.OrdinalIgnoreCase))
                {
                    Debug.WriteLine("OtomeKairo の現行 API には /api/control がないため、REST API 経由の終了要求は送信しません。");
                    return false;
                }

                // 設定から各プロセスのポート番号を取得
                var settings = AppSettings.Instance;
                int? port = processName.ToLower() switch
                {
                    "cocoroshell" => settings.CocoroShellPort,
                    _ => null
                };

                if (port == null)
                {
                    Debug.WriteLine($"{processName} はREST API経由の終了をサポートしていません。");
                    return false;
                }

                // --- shutdown コマンドを JSON で作成 ---
                var shutdownRequest = new
                {
                    action = "shutdown",
                    reason = "CocoroConsoleからの終了要求"
                };
                var json = System.Text.Json.JsonSerializer.Serialize(shutdownRequest);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                // --- CocoroShell のローカル API URL を組み立てる ---
                var apiUrl = $"http://127.0.0.1:{port}/api/control";

                // --- /api/control エンドポイントに POST で送信 ---
                // ConfigureAwait(false) を使用してデッドロックを回避
                using var requestMessage = new HttpRequestMessage(HttpMethod.Post, apiUrl)
                {
                    Content = content
                };
                var response = await httpClient.SendAsync(requestMessage).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    Debug.WriteLine($"{processName} にREST API経由で終了シグナルを送信しました。");
                    return true;
                }
                else
                {
                    Debug.WriteLine($"{processName} への終了シグナル送信に失敗しました。ステータス: {response.StatusCode}");
                    return false;
                }
            }
            catch (HttpRequestException ex)
            {
                Debug.WriteLine($"{processName} への接続に失敗しました: {ex.Message}");
                return false;
            }
            catch (TaskCanceledException)
            {
                Debug.WriteLine($"{processName} への終了シグナル送信がタイムアウトしました。");
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"REST API経由の協調的終了中にエラーが発生しました: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// OS の Kill によりプロセスを強制終了します
        /// </summary>
        /// <param name="processName">プロセス名（拡張子なし）</param>
        /// <returns>Kill を実行した場合は true</returns>
        private static bool TryForceTermination(string processName)
        {
            try
            {
                // --- 対象が実行中でなければ何もしない ---
                var processesByName = Process.GetProcessesByName(processName);
                if (processesByName.Length == 0)
                {
                    return false;
                }

                try
                {
                    // --- 設定からポートが分かる場合は PID を優先して Kill（別名プロセス誤爆の防止） ---
                    var settings = AppSettings.Instance;
                    int? port = processName.ToLower() switch
                    {
                        "otomekairo" => settings.OtomeKairoPort,
                        "cocoroshell" => settings.CocoroShellPort,
                        _ => null
                    };

                    int? processId = null;
                    if (port.HasValue)
                    {
                        processId = GetProcessIdByPort(port.Value);
                    }

                    if (processId.HasValue)
                    {
                        using var p = Process.GetProcessById(processId.Value);
                        p.Kill(entireProcessTree: true);
                        return true;
                    }

                    // --- PID が取れない場合は、名前一致の全プロセスを Kill ---
                    foreach (var p in processesByName)
                    {
                        try
                        {
                            p.Kill(entireProcessTree: true);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"強制終了中にエラーが発生しました: {ex.Message}");
                        }
                    }

                    return true;
                }
                finally
                {
                    // --- GetProcessesByName で取得したプロセスを破棄 ---
                    foreach (var p in processesByName)
                    {
                        p.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"強制終了中にエラーが発生しました: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// OS の Kill によりプロセスを強制終了します（非同期版）
        /// </summary>
        /// <param name="processName">プロセス名（拡張子なし）</param>
        /// <returns>Kill を実行した場合は true</returns>
        private static async Task<bool> TryForceTerminationAsync(string processName)
        {
            // --- Kill は同期 API のため、UI を止めないよう Task.Run に逃がす ---
            return await Task.Run(() => TryForceTermination(processName)).ConfigureAwait(false);
        }

        /// <summary>
        /// 外部アプリケーションを起動します
        /// </summary>
        /// <param name="exeName">アプリケーションexe名</param>
        /// <param name="relativeDir">相対ディレクトリ</param>
        /// <param name="operation">プロセス操作の種類（終了のみか再起動か）</param>
        /// <param name="createWindow">ウィンドウを作成するかどうか（デフォルトはfalse：コンソールを非表示）</param>
        public static void LaunchExternalApplication(string exeName, string? relativeDir = null, ProcessOperation operation = ProcessOperation.RestartIfRunning, bool createWindow = false)
        {
            LaunchExternalApplicationAsync(exeName, relativeDir, operation, createWindow).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 外部アプリケーションを起動します（非同期版）
        /// </summary>
        /// <param name="exeName">アプリケーションexe名</param>
        /// <param name="relativeDir">相対ディレクトリ</param>
        /// <param name="operation">プロセス操作の種類（終了のみか再起動か）</param>
        /// <param name="createWindow">ウィンドウを作成するかどうか（デフォルトはfalse：コンソールを非表示）</param>
        public static async Task LaunchExternalApplicationAsync(string exeName, string? relativeDir = null, ProcessOperation operation = ProcessOperation.RestartIfRunning, bool createWindow = false)
        {
            try
            {
                // 実行ファイルパスの構築
                string fullPath;
                if (relativeDir != null)
                {
                    fullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relativeDir, exeName);
                }
                else
                {
                    fullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, exeName);
                }

                // 同名の実行中プロセスをチェックして終了または再起動
                string processName = Path.GetFileNameWithoutExtension(exeName);

                // --- OtomeKairo が外部利用設定の場合、ローカル起動/終了は行わない ---
                if (processName.Equals("OtomeKairo", StringComparison.OrdinalIgnoreCase) &&
                    !AppSettings.Instance.IsOtomeKairoLocal())
                {
                    Debug.WriteLine("[ProcessHelper] OtomeKairo は外部利用設定のため、ローカルプロセス操作をスキップします。");
                    return;
                }

                // 再起動の場合は、既存プロセスを終了して完全に終了するまで待機
                if (operation == ProcessOperation.RestartIfRunning)
                {
                    Debug.WriteLine($"[ProcessHelper] {processName} の再起動を開始します...");

                    // --- 先に実行中かどうかを確認し、失敗時のログを正しくする ---
                    bool wasRunning = Process.GetProcessesByName(processName).Length > 0;
                    bool wasTerminated = await ExitProcessAsync(processName, operation).ConfigureAwait(false);
                    if (wasTerminated)
                    {
                        Debug.WriteLine($"[ProcessHelper] {processName} の終了を確認しました。新しいプロセスを起動します。");
                    }
                    else
                    {
                        if (wasRunning)
                        {
                            UIHelper.ShowError("再起動エラー", $"{processName} の終了に失敗したため再起動できませんでした。");
                            return;
                        }
                        Debug.WriteLine($"[ProcessHelper] {processName} は実行されていませんでした。新しいプロセスを起動します。");
                    }
                }

                // 終了のみの場合は起動しない
                if (operation == ProcessOperation.Terminate)
                {
                    await ExitProcessAsync(processName, operation).ConfigureAwait(false);
                    return;
                }

                // ファイルの存在確認
                if (!File.Exists(fullPath))
                {
                    UIHelper.ShowError("起動エラー", $"{exeName}が見つかないため正常動作しません。パス: {fullPath}");
                    return;
                }

                // プロセス起動のためのパラメータを設定
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = fullPath,
                    UseShellExecute = false, // 環境により起動しなくなる問題の対策（この操作はユーザーによって取り消されました）
                    WorkingDirectory = Path.GetDirectoryName(fullPath), // 環境により起動しなくなる問題の対策（この操作はユーザーによって取り消されました）
                    CreateNoWindow = !createWindow, // ウィンドウの作成制御
                    WindowStyle = createWindow ? ProcessWindowStyle.Normal : ProcessWindowStyle.Hidden // ウィンドウスタイルの設定
                };


                // プロセスを起動
                var newProcess = Process.Start(startInfo);
                if (newProcess != null)
                {
                    Debug.WriteLine($"[ProcessHelper] {processName} を正常に起動しました。PID: {newProcess.Id}");

                    // 起動後、少し待機してプロセスが安定するのを待つ
                    await Task.Delay(500).ConfigureAwait(false);
                }
                else
                {
                    Debug.WriteLine($"[ProcessHelper] {processName} の起動に失敗しました。");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ProcessHelper] {exeName} 起動中にエラーが発生しました: {ex.Message}");
                UIHelper.ShowError($"{exeName}起動エラー", ex.Message);
            }
        }

    }
}
