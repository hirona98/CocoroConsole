using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using CocoroConsole.Utilities;
using CocoroConsole.Windows;
using Microsoft.Win32;

namespace CocoroConsole.Controls
{
    public partial class MemorySettingsControl : UserControl
    {
        public MemorySettingsControl()
        {
            InitializeComponent();
        }

        /// <summary>
        /// UserDataディレクトリを探索して見つける（AppSettingsのロジックを簡略化して流用）
        /// </summary>
        private string FindUserDataDirectory()
        {
            var baseDirectory = AppContext.BaseDirectory;

            // 探索するパスの配列
            string[] searchPaths = {
#if !DEBUG
                Path.Combine(baseDirectory, "UserData"),
#endif
                Path.Combine(baseDirectory, "..", "UserData"),
                Path.Combine(baseDirectory, "..", "..", "UserData"),
                Path.Combine(baseDirectory, "..", "..", "..", "UserData"),
                Path.Combine(baseDirectory, "..", "..", "..", "..", "UserData")
            };

            foreach (var path in searchPaths)
            {
                var fullPath = Path.GetFullPath(path);
                if (Directory.Exists(fullPath))
                {
                    return fullPath;
                }
            }

            // 見つからない場合は、最初のパスを使用
            return Path.GetFullPath(searchPaths[0]);
        }

        private async void BackupMemoryButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "すべての記憶データをバックアップします。\n" +
                "バックアップ中はCocoroGhostが一時停止します。\n\n" +
                "実行しますか？",
                "記憶のバックアップ確認",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            // プログレスダイアログを表示
            var progressDialog = new SimpleProgressDialog
            {
                Owner = Window.GetWindow(this)
            };

            // ボタンを無効化
            BackupMemoryButton.IsEnabled = false;

            // タイムスタンプ付きバックアップフォルダ名を生成
            string timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
            string backupDirName = $"BackupMemory_{timestamp}";

            try
            {
                // ダイアログを表示
                progressDialog.Show();
                progressDialog.MessageText.Text = "CocoroGhostを停止しています...";

                // バックアップ処理を実行
                await Task.Run(async () =>
                {
                    // CocoroGhostプロセスを停止
                    ProcessHelper.LaunchExternalApplication("CocoroGhost.exe", "CocoroGhost", ProcessOperation.Terminate, false);

                    // プロセスの停止を待つ（ポーリング）
                    int waitCount = 0;
                    while (waitCount < 60) // 最大60秒待機
                    {
                        await Task.Delay(1000);
                        var processes = Process.GetProcessesByName("CocoroGhost");
                        if (processes.Length == 0)
                        {
                            break;
                        }
                        foreach (var process in processes)
                        {
                            process.Dispose();
                        }
                        waitCount++;
                    }

                    // 少し待つ（ファイルハンドルが解放されるのを確実にするため）
                    await Task.Delay(2000);

                    // UI更新
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        progressDialog.MessageText.Text = "バックアップを作成しています...";
                    });

                    // フォルダパスを取得
                    string userDataPath = FindUserDataDirectory();
                    string baseDirectory = Path.GetDirectoryName(userDataPath) ?? AppContext.BaseDirectory;
                    string backupPath = Path.Combine(baseDirectory, backupDirName);

                    string memoryPath = Path.Combine(userDataPath, "Memory");
                    string neo4jDataPath = Path.Combine(baseDirectory, "CocoroGhost", "neo4j", "data");

                    string backupMemoryPath = Path.Combine(backupPath, "Memory");
                    string backupNeo4jPath = Path.Combine(backupPath, "neo4j_data");

                    // バックアップディレクトリを作成
                    Directory.CreateDirectory(backupPath);
                    Debug.WriteLine($"バックアップディレクトリ作成: {backupPath}");

                    // Memoryフォルダのコピー
                    if (Directory.Exists(memoryPath))
                    {
                        try
                        {
                            DirectoryCopy(memoryPath, backupMemoryPath, true);
                            Debug.WriteLine($"Memoryフォルダコピー完了: {memoryPath} -> {backupMemoryPath}");
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Memoryコピーエラー: {ex.Message}");
                            throw new Exception($"Memoryフォルダのコピーに失敗しました: {ex.Message}");
                        }
                    }
                    else
                    {
                        Debug.WriteLine($"Memoryフォルダが存在しません: {memoryPath}");
                    }

                    // Neo4j dataフォルダのコピー
                    if (Directory.Exists(neo4jDataPath))
                    {
                        try
                        {
                            DirectoryCopy(neo4jDataPath, backupNeo4jPath, true);
                            Debug.WriteLine($"Neo4j dataコピー完了: {neo4jDataPath} -> {backupNeo4jPath}");
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Neo4j dataコピーエラー: {ex.Message}");
                            throw new Exception($"Neo4jデータフォルダのコピーに失敗しました: {ex.Message}");
                        }
                    }
                    else
                    {
                        Debug.WriteLine($"Neo4j dataフォルダが存在しません: {neo4jDataPath}");
                    }

                    // UI更新
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        progressDialog.MessageText.Text = "CocoroGhostを再起動しています...";
                    });

                    await Task.Delay(1000);
                });

                // ダイアログを閉じる
                progressDialog.Close();

                // CocoroGhostを再起動
                ProcessHelper.LaunchExternalApplication("CocoroGhost.exe", "CocoroGhost", ProcessOperation.RestartIfRunning, false);

                // 完了メッセージ
                MessageBox.Show(
                    $"記憶データのバックアップが完了しました。\n\n" +
                    $"バックアップ先: {backupDirName}\n" +
                    $"CocoroGhostの再起動を開始しました。",
                    "バックアップ完了",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                progressDialog.Close();
                MessageBox.Show(
                    $"記憶のバックアップ中にエラーが発生しました:\n{ex.Message}",
                    "エラー",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                // ボタンを有効化
                BackupMemoryButton.IsEnabled = true;
            }
        }

        /// <summary>
        /// ディレクトリを再帰的にコピーする
        /// </summary>
        private void DirectoryCopy(string sourceDirName, string destDirName, bool copySubDirs)
        {
            // ソースディレクトリの情報を取得
            DirectoryInfo dir = new DirectoryInfo(sourceDirName);

            if (!dir.Exists)
            {
                throw new DirectoryNotFoundException($"ソースディレクトリが存在しません: {sourceDirName}");
            }

            // ディレクトリが存在しない場合は作成
            if (!Directory.Exists(destDirName))
            {
                Directory.CreateDirectory(destDirName);
            }

            // ファイルをコピー
            FileInfo[] files = dir.GetFiles();
            foreach (FileInfo file in files)
            {
                string temppath = Path.Combine(destDirName, file.Name);
                file.CopyTo(temppath, true);
            }

            // サブディレクトリをコピー
            if (copySubDirs)
            {
                DirectoryInfo[] dirs = dir.GetDirectories();
                foreach (DirectoryInfo subdir in dirs)
                {
                    string temppath = Path.Combine(destDirName, subdir.Name);
                    DirectoryCopy(subdir.FullName, temppath, copySubDirs);
                }
            }
        }

        private async void RestoreMemoryButton_Click(object sender, RoutedEventArgs e)
        {
            // フォルダ選択ダイアログを表示
            var dialog = new OpenFolderDialog
            {
                Title = "バックアップフォルダを選択してください"
            };

            // UserDataと同一階層に初期フォルダを設定
            try
            {
                string userDataPath = FindUserDataDirectory();
                string baseDirectory = Path.GetDirectoryName(userDataPath) ?? AppContext.BaseDirectory;
                dialog.InitialDirectory = baseDirectory;
            }
            catch
            {
                // エラーが発生した場合はデフォルトのパスを使用
                dialog.InitialDirectory = AppContext.BaseDirectory;
            }

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            string selectedFolder = dialog.FolderName;
            string folderName = Path.GetFileName(selectedFolder);

            // BackupMemory_* フォルダかどうかチェック
            if (!folderName.StartsWith("BackupMemory_"))
            {
                MessageBox.Show(
                    "選択されたフォルダはバックアップフォルダではありません。\n" +
                    "BackupMemory_YYYYMMDDHHMMSS 形式のフォルダを選択してください。",
                    "エラー",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            // バックアップデータの存在確認
            string backupMemoryPath = Path.Combine(selectedFolder, "Memory");
            string backupNeo4jPath = Path.Combine(selectedFolder, "neo4j_data");

            if (!Directory.Exists(backupMemoryPath) && !Directory.Exists(backupNeo4jPath))
            {
                MessageBox.Show(
                    "選択されたフォルダにバックアップデータが見つかりません。\n" +
                    "Memory または neo4j_data フォルダが必要です。",
                    "エラー",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            // 確認ダイアログ
            var result = MessageBox.Show(
                $"以下のバックアップから記憶データを復元します。\n" +
                $"フォルダ: {folderName}\n\n" +
                $"現在のすべての記憶データが上書きされます。\n" +
                $"この操作は元に戻せません。\n\n" +
                $"実行しますか？",
                "記憶の復元確認",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            // プログレスダイアログを表示
            var progressDialog = new SimpleProgressDialog
            {
                Owner = Window.GetWindow(this)
            };

            // ボタンを無効化
            RestoreMemoryButton.IsEnabled = false;

            try
            {
                // ダイアログを表示
                progressDialog.Show();
                progressDialog.MessageText.Text = "CocoroGhostを停止しています...";

                // 復元処理を実行
                await Task.Run(async () =>
                {
                    // CocoroGhostプロセスを停止
                    ProcessHelper.LaunchExternalApplication("CocoroGhost.exe", "CocoroGhost", ProcessOperation.Terminate, false);

                    // プロセスの停止を待つ（ポーリング）
                    int waitCount = 0;
                    while (waitCount < 60) // 最大60秒待機
                    {
                        await Task.Delay(1000);
                        var processes = Process.GetProcessesByName("CocoroGhost");
                        if (processes.Length == 0)
                        {
                            break;
                        }
                        foreach (var process in processes)
                        {
                            process.Dispose();
                        }
                        waitCount++;
                    }

                    // 少し待つ（ファイルハンドルが解放されるのを確実にするため）
                    await Task.Delay(2000);

                    // UI更新
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        progressDialog.MessageText.Text = "既存の記憶データを削除しています...";
                    });

                    // 現在のパスを取得
                    string userDataPath = FindUserDataDirectory();
                    string baseDirectory = Path.GetDirectoryName(userDataPath) ?? AppContext.BaseDirectory;

                    string currentMemoryPath = Path.Combine(userDataPath, "Memory");
                    string currentNeo4jPath = Path.Combine(baseDirectory, "CocoroGhost", "neo4j", "data");

                    // 既存のデータを削除
                    if (Directory.Exists(currentMemoryPath))
                    {
                        try
                        {
                            Directory.Delete(currentMemoryPath, true);
                            Debug.WriteLine($"既存Memory削除完了: {currentMemoryPath}");
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"既存Memory削除エラー: {ex.Message}");
                        }
                    }

                    if (Directory.Exists(currentNeo4jPath))
                    {
                        try
                        {
                            Directory.Delete(currentNeo4jPath, true);
                            Debug.WriteLine($"既存Neo4j data削除完了: {currentNeo4jPath}");
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"既存Neo4j data削除エラー: {ex.Message}");
                        }
                    }

                    // UI更新
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        progressDialog.MessageText.Text = "バックアップから復元しています...";
                    });

                    // バックアップから復元
                    if (Directory.Exists(backupMemoryPath))
                    {
                        try
                        {
                            DirectoryCopy(backupMemoryPath, currentMemoryPath, true);
                            Debug.WriteLine($"Memory復元完了: {backupMemoryPath} -> {currentMemoryPath}");
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Memory復元エラー: {ex.Message}");
                            throw new Exception($"Memoryフォルダの復元に失敗しました: {ex.Message}");
                        }
                    }

                    if (Directory.Exists(backupNeo4jPath))
                    {
                        try
                        {
                            DirectoryCopy(backupNeo4jPath, currentNeo4jPath, true);
                            Debug.WriteLine($"Neo4j data復元完了: {backupNeo4jPath} -> {currentNeo4jPath}");
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Neo4j data復元エラー: {ex.Message}");
                            throw new Exception($"Neo4jデータフォルダの復元に失敗しました: {ex.Message}");
                        }
                    }

                    // UI更新
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        progressDialog.MessageText.Text = "CocoroGhostを再起動しています...";
                    });

                    await Task.Delay(1000);
                });

                // ダイアログを閉じる
                progressDialog.Close();

                // CocoroGhostを再起動
                ProcessHelper.LaunchExternalApplication("CocoroGhost.exe", "CocoroGhost", ProcessOperation.RestartIfRunning, false);

                // 完了メッセージ
                MessageBox.Show(
                    $"記憶データの復元が完了しました。\n\n" +
                    $"復元元: {folderName}\n" +
                    $"CocoroGhostの再起動を開始しました。",
                    "復元完了",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                progressDialog.Close();
                MessageBox.Show(
                    $"記憶の復元中にエラーが発生しました:\n{ex.Message}",
                    "エラー",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                // ボタンを有効化
                RestoreMemoryButton.IsEnabled = true;
            }
        }

        private async void DeleteMemoryButton_Click(object sender, RoutedEventArgs e)
        {
            // 確認ダイアログを表示
            var result = MessageBox.Show(
                $"全キャラクターのすべての記憶データを削除します。\n" +
                "この操作は元に戻せません。\n\n" +
                "本当に削除しますか？",
                "記憶の削除確認",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            // 二重確認
            result = MessageBox.Show(
                "本当に削除してもよろしいですか？\n" +
                "すべての記憶とデータベースが失われます。",
                "最終確認",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            // 最低限ダイアログを表示
            var progressDialog = new SimpleProgressDialog
            {
                Owner = Window.GetWindow(this)
            };

            // ボタンを無効化
            DeleteMemoryButton.IsEnabled = false;

            try
            {
                // ダイアログを表示
                progressDialog.Show();
                progressDialog.MessageText.Text = "CocoroGhostを停止しています...";

                // 削除処理を実行
                await Task.Run(async () =>
                {
                    // CocoroGhostプロセスを停止
                    ProcessHelper.LaunchExternalApplication("CocoroGhost.exe", "CocoroGhost", ProcessOperation.Terminate, false);

                    // プロセスの停止を待つ（ポーリング）
                    int waitCount = 0;
                    while (waitCount < 60) // 最大60秒待機
                    {
                        await Task.Delay(1000);
                        var processes = Process.GetProcessesByName("CocoroGhost");
                        if (processes.Length == 0)
                        {
                            break;
                        }
                        foreach (var process in processes)
                        {
                            process.Dispose();
                        }
                        waitCount++;
                    }

                    // 少し待つ（ファイルハンドルが解放されるのを確実にするため）
                    await Task.Delay(2000);

                    // UI更新
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        progressDialog.MessageText.Text = "記憶データを削除しています...";
                    });

                    // フォルダを削除（AppSettingsの探索ロジックを簡略化して流用）
                    string userDataPath = FindUserDataDirectory();
                    string memoryPath = Path.Combine(userDataPath, "Memory");

                    // UserDataと同一階層のCocoroGhostディレクトリからneo4jパスを取得
                    string baseDirectory = Path.GetDirectoryName(userDataPath) ?? AppContext.BaseDirectory;
                    string neo4jDataPath = Path.Combine(baseDirectory, "CocoroGhost", "neo4j", "data");

                    // Memoryフォルダの削除
                    if (Directory.Exists(memoryPath))
                    {
                        try
                        {
                            Directory.Delete(memoryPath, true);
                            Debug.WriteLine($"削除完了: {memoryPath}");
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Memory削除エラー: {ex.Message}");
                            throw new Exception($"Memoryフォルダの削除に失敗しました: {ex.Message}");
                        }
                    }

                    // Neo4j dataフォルダの削除
                    if (Directory.Exists(neo4jDataPath))
                    {
                        try
                        {
                            Directory.Delete(neo4jDataPath, true);
                            Debug.WriteLine($"削除完了: {neo4jDataPath}");
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Neo4j data削除エラー: {ex.Message}");
                            throw new Exception($"Neo4jデータフォルダの削除に失敗しました: {ex.Message}");
                        }
                    }

                    await Task.Delay(1000);
                });

                // ダイアログを閉じる
                progressDialog.Close();

                // 完了メッセージ
                MessageBox.Show(
                    "記憶データの削除が完了しました。\n\n" +
                    "新しい記憶データベースを作成するには、\n" +
                    "CocoroAIを再起動してください。",
                    "削除完了",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                progressDialog.Close();
                MessageBox.Show(
                    $"記憶の削除中にエラーが発生しました:\n{ex.Message}",
                    "エラー",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                // ボタンを有効化
                DeleteMemoryButton.IsEnabled = true;
            }
        }
    }
}