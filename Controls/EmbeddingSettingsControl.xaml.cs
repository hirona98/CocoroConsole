using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using CocoroConsole.Models.CocoroGhostApi;
using CocoroConsole.Utilities;
using CocoroConsole.Windows;
using Microsoft.Win32;

namespace CocoroConsole.Controls
{
    public partial class EmbeddingSettingsControl : UserControl
    {
        private bool _isInitializing = false;
        private EmbeddingPreset? _currentPreset = null;

        public event EventHandler? SettingsChanged;

        public EmbeddingSettingsControl()
        {
            InitializeComponent();
        }

        public void LoadSettings(EmbeddingPreset? preset)
        {
            _isInitializing = true;
            _currentPreset = preset;

            try
            {
                if (preset == null)
                {
                    ClearSettings();
                    return;
                }

                MemoryIdTextBox.Text = preset.EmbeddingPresetName ?? string.Empty;
                EmbeddingApiKeyPasswordBox.Password = preset.EmbeddingModelApiKey ?? string.Empty;
                EmbeddingModelTextBox.Text = preset.EmbeddingModel ?? string.Empty;
                EmbeddingBaseUrlTextBox.Text = preset.EmbeddingBaseUrl ?? string.Empty;
                EmbeddingDimensionTextBox.Text = preset.EmbeddingDimension?.ToString() ?? "1536";
                SimilarEpisodesLimitTextBox.Text = preset.SimilarEpisodesLimit?.ToString() ?? "5";
            }
            finally
            {
                _isInitializing = false;
            }
        }

        public EmbeddingPreset? GetSettings()
        {
            if (_currentPreset == null)
            {
                return null;
            }

            EmbeddingPreset preset = new EmbeddingPreset
            {
                EmbeddingPresetId = _currentPreset.EmbeddingPresetId,
                EmbeddingPresetName = MemoryIdTextBox.Text,
                EmbeddingModelApiKey = string.IsNullOrWhiteSpace(EmbeddingApiKeyPasswordBox.Password) ? null : EmbeddingApiKeyPasswordBox.Password,
                EmbeddingModel = EmbeddingModelTextBox.Text,
                EmbeddingBaseUrl = string.IsNullOrWhiteSpace(EmbeddingBaseUrlTextBox.Text) ? null : EmbeddingBaseUrlTextBox.Text,
                EmbeddingDimension = int.TryParse(EmbeddingDimensionTextBox.Text, out int dimension) ? dimension : 1536,
                SimilarEpisodesLimit = int.TryParse(SimilarEpisodesLimitTextBox.Text, out int limit) ? limit : 5
            };

            return preset;
        }

        private void ClearSettings()
        {
            MemoryIdTextBox.Text = string.Empty;
            EmbeddingApiKeyPasswordBox.Password = string.Empty;
            EmbeddingModelTextBox.Text = string.Empty;
            EmbeddingBaseUrlTextBox.Text = string.Empty;
            EmbeddingDimensionTextBox.Text = "1536";
            SimilarEpisodesLimitTextBox.Text = "5";
        }

        private void OnSettingChanged(object sender, RoutedEventArgs e)
        {
            if (!_isInitializing)
            {
                SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private void OnSettingChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isInitializing)
            {
                SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private string FindUserDataDirectory()
        {
            string baseDirectory = AppContext.BaseDirectory;

            string[] searchPaths = {
#if !DEBUG
                Path.Combine(baseDirectory, "UserData"),
#endif
                Path.Combine(baseDirectory, "..", "UserData"),
                Path.Combine(baseDirectory, "..", "..", "UserData"),
                Path.Combine(baseDirectory, "..", "..", "..", "UserData"),
                Path.Combine(baseDirectory, "..", "..", "..", "..", "UserData")
            };

            foreach (string path in searchPaths)
            {
                string fullPath = Path.GetFullPath(path);
                if (Directory.Exists(fullPath))
                {
                    return fullPath;
                }
            }

            return Path.GetFullPath(searchPaths[0]);
        }

        private async void BackupMemoryButton_Click(object sender, RoutedEventArgs e)
        {
            MessageBoxResult result = MessageBox.Show(
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

            SimpleProgressDialog progressDialog = new SimpleProgressDialog
            {
                Owner = Window.GetWindow(this)
            };

            BackupMemoryButton.IsEnabled = false;

            string timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
            string backupDirName = $"BackupMemory_{timestamp}";

            try
            {
                progressDialog.Show();
                progressDialog.MessageText.Text = "CocoroGhostを停止しています...";

                await Task.Run(async () =>
                {
                    ProcessHelper.LaunchExternalApplication("CocoroGhost.exe", "CocoroGhost", ProcessOperation.Terminate, false);

                    int waitCount = 0;
                    while (waitCount < 60)
                    {
                        await Task.Delay(1000);
                        Process[] processes = Process.GetProcessesByName("CocoroGhost");
                        if (processes.Length == 0)
                        {
                            break;
                        }
                        foreach (Process process in processes)
                        {
                            process.Dispose();
                        }
                        waitCount++;
                    }

                    await Task.Delay(2000);

                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        progressDialog.MessageText.Text = "バックアップを作成しています...";
                    });

                    string userDataPath = FindUserDataDirectory();
                    string baseDirectory = Path.GetDirectoryName(userDataPath) ?? AppContext.BaseDirectory;
                    string backupPath = Path.Combine(baseDirectory, backupDirName);

                    string memoryPath = Path.Combine(userDataPath, "Memory");
                    string neo4jDataPath = Path.Combine(baseDirectory, "CocoroGhost", "neo4j", "data");

                    string backupMemoryPath = Path.Combine(backupPath, "Memory");
                    string backupNeo4jPath = Path.Combine(backupPath, "neo4j_data");

                    Directory.CreateDirectory(backupPath);
                    Debug.WriteLine($"バックアップディレクトリ作成: {backupPath}");

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

                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        progressDialog.MessageText.Text = "CocoroGhostを再起動しています...";
                    });

                    await Task.Delay(1000);
                });

                progressDialog.Close();

                ProcessHelper.LaunchExternalApplication("CocoroGhost.exe", "CocoroGhost", ProcessOperation.RestartIfRunning, false);

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
                BackupMemoryButton.IsEnabled = true;
            }
        }

        private void DirectoryCopy(string sourceDirName, string destDirName, bool copySubDirs)
        {
            DirectoryInfo dir = new DirectoryInfo(sourceDirName);

            if (!dir.Exists)
            {
                throw new DirectoryNotFoundException($"ソースディレクトリが存在しません: {sourceDirName}");
            }

            if (!Directory.Exists(destDirName))
            {
                Directory.CreateDirectory(destDirName);
            }

            FileInfo[] files = dir.GetFiles();
            foreach (FileInfo file in files)
            {
                string temppath = Path.Combine(destDirName, file.Name);
                file.CopyTo(temppath, true);
            }

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
            OpenFolderDialog dialog = new OpenFolderDialog
            {
                Title = "バックアップフォルダを選択してください"
            };

            try
            {
                string userDataPath = FindUserDataDirectory();
                string baseDirectory = Path.GetDirectoryName(userDataPath) ?? AppContext.BaseDirectory;
                dialog.InitialDirectory = baseDirectory;
            }
            catch
            {
                dialog.InitialDirectory = AppContext.BaseDirectory;
            }

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            string selectedFolder = dialog.FolderName;
            string folderName = Path.GetFileName(selectedFolder);

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

            MessageBoxResult result = MessageBox.Show(
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

            SimpleProgressDialog progressDialog = new SimpleProgressDialog
            {
                Owner = Window.GetWindow(this)
            };

            RestoreMemoryButton.IsEnabled = false;

            try
            {
                progressDialog.Show();
                progressDialog.MessageText.Text = "CocoroGhostを停止しています...";

                await Task.Run(async () =>
                {
                    ProcessHelper.LaunchExternalApplication("CocoroGhost.exe", "CocoroGhost", ProcessOperation.Terminate, false);

                    int waitCount = 0;
                    while (waitCount < 60)
                    {
                        await Task.Delay(1000);
                        Process[] processes = Process.GetProcessesByName("CocoroGhost");
                        if (processes.Length == 0)
                        {
                            break;
                        }
                        foreach (Process process in processes)
                        {
                            process.Dispose();
                        }
                        waitCount++;
                    }

                    await Task.Delay(2000);

                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        progressDialog.MessageText.Text = "既存の記憶データを削除しています...";
                    });

                    string userDataPath = FindUserDataDirectory();
                    string baseDirectory = Path.GetDirectoryName(userDataPath) ?? AppContext.BaseDirectory;

                    string currentMemoryPath = Path.Combine(userDataPath, "Memory");
                    string currentNeo4jPath = Path.Combine(baseDirectory, "CocoroGhost", "neo4j", "data");

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

                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        progressDialog.MessageText.Text = "バックアップから復元しています...";
                    });

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

                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        progressDialog.MessageText.Text = "CocoroGhostを再起動しています...";
                    });

                    await Task.Delay(1000);
                });

                progressDialog.Close();

                ProcessHelper.LaunchExternalApplication("CocoroGhost.exe", "CocoroGhost", ProcessOperation.RestartIfRunning, false);

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
                RestoreMemoryButton.IsEnabled = true;
            }
        }

        private async void DeleteMemoryButton_Click(object sender, RoutedEventArgs e)
        {
            MessageBoxResult result = MessageBox.Show(
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

            SimpleProgressDialog progressDialog = new SimpleProgressDialog
            {
                Owner = Window.GetWindow(this)
            };

            DeleteMemoryButton.IsEnabled = false;

            try
            {
                progressDialog.Show();
                progressDialog.MessageText.Text = "CocoroGhostを停止しています...";

                await Task.Run(async () =>
                {
                    ProcessHelper.LaunchExternalApplication("CocoroGhost.exe", "CocoroGhost", ProcessOperation.Terminate, false);

                    int waitCount = 0;
                    while (waitCount < 60)
                    {
                        await Task.Delay(1000);
                        Process[] processes = Process.GetProcessesByName("CocoroGhost");
                        if (processes.Length == 0)
                        {
                            break;
                        }
                        foreach (Process process in processes)
                        {
                            process.Dispose();
                        }
                        waitCount++;
                    }

                    await Task.Delay(2000);

                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        progressDialog.MessageText.Text = "記憶データを削除しています...";
                    });

                    string userDataPath = FindUserDataDirectory();
                    string memoryPath = Path.Combine(userDataPath, "Memory");

                    string baseDirectory = Path.GetDirectoryName(userDataPath) ?? AppContext.BaseDirectory;
                    string neo4jDataPath = Path.Combine(baseDirectory, "CocoroGhost", "neo4j", "data");

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

                progressDialog.Close();

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
                DeleteMemoryButton.IsEnabled = true;
            }
        }
    }
}
