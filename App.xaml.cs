using CocoroConsole.Services;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using NotifyIcon = System.Windows.Forms.NotifyIcon;

namespace CocoroConsole
{
    /// <summary>
    /// App.xaml の相互作用ロジック
    /// Mutexによる同時起動防止とNamedPipeによるプロセス間通信を実装
    /// </summary>
    public partial class App : Application
    {
        private NotifyIcon? _notifyIcon;
        private static readonly string PipeName = GetPipeNameFromExecutable();
        private Thread? _pipeServerThread;
        private CancellationTokenSource? _pipeServerCancellationTokenSource;
        private static Mutex? _mutex;

        protected override void OnStartup(StartupEventArgs e)
        {
            // Mutexによる二重起動チェック
            string mutexName = $"Global\\{GetPipeNameFromExecutable()}";
            bool createdNew;

            try
            {
                _mutex = new Mutex(true, mutexName, out createdNew);

                if (!createdNew)
                {
                    // 既に起動している場合、既存のプロセスにメッセージを送信
                    try
                    {
                        using (var pipeClient = new NamedPipeClientStream(".", PipeName, PipeDirection.Out))
                        {
                            pipeClient.Connect(1000); // 1秒でタイムアウト
                            using (var writer = new StreamWriter(pipeClient))
                            {
                                writer.WriteLine("SHOW_WINDOW");
                                writer.Flush();
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"既存プロセスへの接続に失敗しました: {ex.Message}",
                            "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                    }

                    // 自プロセスを終了
                    Environment.Exit(0);
                    return;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Mutex作成エラー: {ex.Message}",
                    "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                Environment.Exit(1);
                return;
            }

            // パイプサーバーを開始
            StartPipeServer();

            // システムトレイアイコンの初期化
            InitializeNotifyIcon();

            // 未処理の例外ハンドラを登録
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            Application.Current.DispatcherUnhandledException += Application_DispatcherUnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

            // メインウィンドウを作成するが、表示はしない
            MainWindow mainWindow = new MainWindow();
            Current.MainWindow = mainWindow; // MainWindowプロパティに明示的に設定

            // コマンドライン引数をチェックして、表示フラグがある場合のみ表示する
            bool showWindow = e.Args.Any(arg => arg.ToLower() == "/show" || arg.ToLower() == "-show");

            // デバッグモードの場合は常に表示
#if DEBUG
            showWindow = true;
#endif

            if (showWindow)
            {
                mainWindow.Show();
                mainWindow.WindowState = WindowState.Normal;
                mainWindow.Activate();
            }
        }

        // 名前付きパイプサーバーを開始
        private void StartPipeServer()
        {
            _pipeServerCancellationTokenSource = new CancellationTokenSource();
            _pipeServerThread = new Thread(() =>
                PipeServerLoopAsync(_pipeServerCancellationTokenSource.Token)
                    .GetAwaiter()
                    .GetResult()
            )
            {
                IsBackground = true,
                Name = "PipeServerThread"
            };
            _pipeServerThread.Start();
        }

        // パイプサーバーループ（終了時に確実に抜けるため async + cancellation 対応）
        private async Task PipeServerLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // WaitForConnection をキャンセル可能にするため Asynchronous を指定
                    using (var pipeServer = new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.In,
                        1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous
                    ))
                    {
                        // 終了時の Cancel で待機解除できるようにする
                        await pipeServer.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);

                        using (var reader = new StreamReader(pipeServer))
                        {
                            string? message = reader.ReadLine();
                            if (message == "SHOW_WINDOW")
                            {
                                // UIスレッドでメインウィンドウを表示
                                Dispatcher.Invoke(() => ShowMainWindow());
                            }
                        }
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // 終了要求なのでループを抜ける
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"パイプサーバーエラー: {ex.Message}");
                    // 終了要求中は待機せず終了
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }
                    Thread.Sleep(1000); // エラー時は少し待機
                }
            }
        }


        protected override void OnExit(ExitEventArgs e)
        {
            // パイプサーバースレッドを終了
            _pipeServerCancellationTokenSource?.Cancel();
            _pipeServerThread?.Join(1000); // 最大1秒待機

            // システムトレイアイコンの解放
            _notifyIcon?.Dispose();

            // Mutexを解放
            _mutex?.ReleaseMutex();
            _mutex?.Dispose();

            // アプリケーション終了時の処理
            try
            {
                // 設定を保存
                AppSettings.Instance.SaveSettings();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"設定保存エラー: {ex.Message}");
            }

            base.OnExit(e);
        }

        /// <summary>
        /// システムトレイアイコンの初期化
        /// </summary>
        private void InitializeNotifyIcon()
        {
            try
            {
                _notifyIcon = new NotifyIcon
                {
                    Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!),
                    Visible = true,
                    Text = "CocoroAI"
                };

                // コンテキストメニューの作成
                var contextMenu = new System.Windows.Forms.ContextMenuStrip();

                // 表示メニュー項目
                var showMenuItem = new System.Windows.Forms.ToolStripMenuItem
                {
                    Text = "表示"
                };
                showMenuItem.Click += (s, e) => ShowMainWindow();
                contextMenu.Items.Add(showMenuItem);

                // 終了メニュー項目
                var exitMenuItem = new System.Windows.Forms.ToolStripMenuItem
                {
                    Text = "終了"
                };
                exitMenuItem.Click += async (s, e) => await PerformGracefulShutdownAsync();
                contextMenu.Items.Add(exitMenuItem);

                _notifyIcon.ContextMenuStrip = contextMenu;

                // アイコンをダブルクリックした時のイベント
                _notifyIcon.DoubleClick += (s, e) => ShowMainWindow();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"システムトレイアイコンの初期化エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// 実行ファイル名からパイプ名を生成する
        /// </summary>
        private static string GetPipeNameFromExecutable()
        {
            try
            {
                // 実行ファイルのフルパスを取得
                string exePath = Environment.ProcessPath!;

                // ファイル名（拡張子なし）を取得
                string exeName = Path.GetFileNameWithoutExtension(exePath);

                // パイプ名を生成（ファイル名 + "Pipe"）
                return $"{exeName}Pipe";
            }
            catch
            {
                // エラーが発生した場合はデフォルト名を返す
                return "CocoroConsolePipe";
            }
        }

        /// <summary>
        /// メインウィンドウを表示する
        /// </summary>
        private void ShowMainWindow()
        {
            try
            {
                // メインウィンドウを取得（Applicationのウィンドウコレクションから探す）
                Window? mainWindow = null;

                foreach (Window window in Application.Current.Windows)
                {
                    if (window is MainWindow)
                    {
                        mainWindow = window;
                        break;
                    }
                }

                if (mainWindow != null)
                {
                    // ウィンドウを表示
                    mainWindow.Show();
                    mainWindow.WindowState = WindowState.Normal;
                    mainWindow.Activate();
                    mainWindow.Topmost = true;
                    mainWindow.Topmost = false;
                    mainWindow.Focus();
                }
                else
                {
                    // メインウィンドウが見つからない場合は新しく作成して表示
                    mainWindow = new MainWindow();
                    mainWindow.Show();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ウィンドウ表示エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// 通常スレッドでの未処理例外ハンドラ
        /// </summary>
        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            ShowFatalError(e.ExceptionObject as Exception, "未処理の例外が発生しました");
        }

        /// <summary>
        /// UIスレッドでの未処理例外ハンドラ
        /// </summary>
        private void Application_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            // エラーをユーザーに表示
            MessageBox.Show($"エラーが発生しました: {e.Exception.Message}",
                "エラー", MessageBoxButton.OK, MessageBoxImage.Error);

            // 例外を処理済みとしてマーク（アプリケーションを継続）
            e.Handled = true;
        }

        /// <summary>
        /// 未監視のタスク例外ハンドラ
        /// </summary>
        private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            e.SetObserved(); // 例外を監視済みとしてマーク
        }

        /// <summary>
        /// 致命的エラーを表示し、アプリケーションを終了
        /// </summary>
        private void ShowFatalError(Exception? ex, string message)
        {
            try
            {
                string errorMessage = ex != null ? ex.Message : "不明なエラー";
                MessageBox.Show($"致命的なエラー: {errorMessage}\n\nアプリケーションを終了します。",
                    "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Environment.Exit(1);
            }
        }

        /// <summary>
        /// グレースフルシャットダウンを実行
        /// </summary>
        private async Task PerformGracefulShutdownAsync()
        {
            try
            {
                // --- UIスレッドで MainWindow を取得 ---
                // Dispatcher.InvokeAsync(async ...) の戻り値（Task）を await し忘れると、
                // 「Shutdown が途中で返ってしまい、ウィンドウが残る」状態になりやすい。
                var mainWindow = await Dispatcher.InvokeAsync(() =>
                {
                    foreach (Window window in Application.Current.Windows)
                    {
                        if (window is MainWindow mw)
                        {
                            return mw;
                        }
                    }
                    return null;
                });

                if (mainWindow != null)
                {
                    // --- UIスレッドで MainWindow の終了処理を実行し、最後まで await する ---
                    await (await Dispatcher.InvokeAsync(() => mainWindow.PerformGracefulShutdownAsync()));
                    return;
                }

                // --- メインウィンドウが見つからない場合は通常のシャットダウン ---
                Debug.WriteLine("MainWindowが見つかりません。通常のシャットダウンを実行します。");
                await Dispatcher.InvokeAsync(() => Shutdown());
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"グレースフルシャットダウン中にエラーが発生しました: {ex.Message}");

                // エラーが発生した場合は通常のシャットダウン
                Shutdown();
            }
        }
    }
}
