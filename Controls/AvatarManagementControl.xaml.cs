using CocoroConsole.Communication;
using CocoroConsole.Services;
using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace CocoroConsole.Controls
{
    /// <summary>
    /// アバターの表示設定（プリセット選択と VRM）を編集する。
    /// 音声合成と音声起動ワードは OtomeKairo WebUI で編集する。
    /// </summary>
    public partial class AvatarManagementControl : UserControl
    {
        public event EventHandler? SettingsChanged;
        public event EventHandler? AvatarChanged;

        private int _currentAvatarIndex = -1;
        private bool _isInitialized = false;
        private DispatcherTimer? _avatarNameChangeTimer;
        private const int CHARACTER_NAME_DEBOUNCE_DELAY_MS = 200;

        public AvatarManagementControl()
        {
            InitializeComponent();

            _avatarNameChangeTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(CHARACTER_NAME_DEBOUNCE_DELAY_MS)
            };
            _avatarNameChangeTimer.Tick += AvatarNameChangeTimer_Tick;
        }

        public void Initialize()
        {
            LoadAvatarList();

            if (AvatarSelectComboBox.SelectedIndex >= 0)
            {
                _currentAvatarIndex = AvatarSelectComboBox.SelectedIndex;
                UpdateAvatarUI();
            }

            _isInitialized = true;
        }

        private void LoadAvatarList()
        {
            var appSettings = AppSettings.Instance;
            AvatarSelectComboBox.ItemsSource = appSettings.AvatarList;

            if (appSettings.AvatarList.Count > 0 &&
                appSettings.CurrentAvatarIndex >= 0 &&
                appSettings.CurrentAvatarIndex < appSettings.AvatarList.Count)
            {
                AvatarSelectComboBox.SelectedIndex = appSettings.CurrentAvatarIndex;
            }
        }

        /// <summary>
        /// UI 上の VRM / 表示関連だけを AppSettings へ同期する。
        /// 音声合成・STT・起動ワードは既存値を保持する。
        /// </summary>
        public void SyncCurrentAvatarFromUi()
        {
            if (_currentAvatarIndex < 0 || _currentAvatarIndex >= AppSettings.Instance.AvatarList.Count)
            {
                return;
            }

            var avatar = AppSettings.Instance.AvatarList[_currentAvatarIndex].DeepCopy();
            avatar.modelName = AvatarNameTextBox.Text;
            avatar.vrmFilePath = VRMFilePathTextBox.Text;
            avatar.isConvertMToon = ConvertMToonCheckBox.IsChecked ?? false;
            avatar.isEnableShadowOff = EnableShadowOffCheckBox.IsChecked ?? false;
            avatar.shadowOffMesh = ShadowOffMeshTextBox.Text;
            AppSettings.Instance.AvatarList[_currentAvatarIndex] = avatar;
        }

        public int GetCurrentAvatarIndex()
        {
            return _currentAvatarIndex;
        }

        private void AvatarSelectComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized || AvatarSelectComboBox.SelectedIndex < 0)
            {
                return;
            }

            SyncCurrentAvatarFromUi();
            _currentAvatarIndex = AvatarSelectComboBox.SelectedIndex;
            UpdateAvatarUI();
            AvatarChanged?.Invoke(this, EventArgs.Empty);
        }

        private void UpdateAvatarUI()
        {
            if (_currentAvatarIndex < 0 || _currentAvatarIndex >= AppSettings.Instance.AvatarList.Count)
            {
                return;
            }

            var avatar = AppSettings.Instance.AvatarList[_currentAvatarIndex];
            AvatarNameTextBox.Text = avatar.modelName;
            VRMFilePathTextBox.Text = avatar.vrmFilePath;
            ConvertMToonCheckBox.IsChecked = avatar.isConvertMToon;
            EnableShadowOffCheckBox.IsChecked = avatar.isEnableShadowOff;
            ShadowOffMeshTextBox.Text = avatar.shadowOffMesh;
            ShadowOffMeshTextBox.IsEnabled = avatar.isEnableShadowOff;

            DeleteAvatarButton.IsEnabled = !avatar.isReadOnly;
            VRMFilePathTextBox.IsEnabled = !avatar.isReadOnly;
            BrowseVrmFileButton.IsEnabled = !avatar.isReadOnly;
        }

        private void AddAvatarButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SyncCurrentAvatarFromUi();

                var newName = "新規アバター";
                int avatarNumber = 1;
                while (AppSettings.Instance.AvatarList.Any(c => c.modelName == newName))
                {
                    newName = $"新規アバター{avatarNumber}";
                    avatarNumber++;
                }

                var newAvatar = AppSettings.Instance.CreateAvatarFromDefaults(newName);
                AppSettings.Instance.AvatarList.Add(newAvatar);

                AvatarSelectComboBox.ItemsSource = null;
                AvatarSelectComboBox.ItemsSource = AppSettings.Instance.AvatarList;
                AvatarSelectComboBox.SelectedIndex = AppSettings.Instance.AvatarList.Count - 1;
                SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"アバター追加エラー: {ex.Message}", "エラー",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DeleteAvatarButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_currentAvatarIndex < 0 || _currentAvatarIndex >= AppSettings.Instance.AvatarList.Count)
                {
                    return;
                }

                SyncCurrentAvatarFromUi();
                var avatar = AppSettings.Instance.AvatarList[_currentAvatarIndex];
                if (avatar.isReadOnly)
                {
                    return;
                }

                AppSettings.Instance.AvatarList.RemoveAt(_currentAvatarIndex);
                _currentAvatarIndex = -1;

                AvatarSelectComboBox.ItemsSource = null;
                AvatarSelectComboBox.ItemsSource = AppSettings.Instance.AvatarList;
                if (AppSettings.Instance.AvatarList.Count > 0)
                {
                    AvatarSelectComboBox.SelectedIndex = 0;
                }

                SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"アバター削除エラー: {ex.Message}", "エラー",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DuplicateAvatarButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_currentAvatarIndex < 0 || _currentAvatarIndex >= AppSettings.Instance.AvatarList.Count)
                {
                    return;
                }

                SyncCurrentAvatarFromUi();
                var sourceAvatar = AppSettings.Instance.AvatarList[_currentAvatarIndex];

                var newName = sourceAvatar.modelName + "_copy";
                int copyNumber = 1;
                while (AppSettings.Instance.AvatarList.Any(c => c.modelName == newName))
                {
                    newName = $"{sourceAvatar.modelName}_copy{copyNumber}";
                    copyNumber++;
                }

                // 音声設定を含む既存値を複製し、表示用の識別だけ付け替える。
                var newAvatar = sourceAvatar.DeepCopy();
                newAvatar.avatarId = $"avatar:{Guid.NewGuid():N}";
                newAvatar.modelName = newName;
                newAvatar.isReadOnly = false;

                AppSettings.Instance.AvatarList.Add(newAvatar);

                AvatarSelectComboBox.ItemsSource = null;
                AvatarSelectComboBox.ItemsSource = AppSettings.Instance.AvatarList;
                AvatarSelectComboBox.SelectedIndex = AppSettings.Instance.AvatarList.Count - 1;
                SettingsChanged?.Invoke(this, EventArgs.Empty);

                Debug.WriteLine($"アバター複製: {sourceAvatar.modelName} -> {newName}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"アバター複製エラー: {ex.Message}", "エラー",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BrowseVrmFileButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new OpenFileDialog
                {
                    Filter = "VRM Files (*.vrm)|*.vrm|All Files (*.*)|*.*",
                    Title = "VRMファイルを選択してください"
                };

                if (dialog.ShowDialog() == true)
                {
                    VRMFilePathTextBox.Text = dialog.FileName;
                    if (string.IsNullOrWhiteSpace(AvatarNameTextBox.Text))
                    {
                        AvatarNameTextBox.Text = Path.GetFileNameWithoutExtension(dialog.FileName);
                    }

                    SettingsChanged?.Invoke(this, EventArgs.Empty);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"ファイル選択エラー: {ex.Message}", "エラー",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void EnableShadowOffCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (ShadowOffMeshTextBox != null)
            {
                ShadowOffMeshTextBox.IsEnabled = true;
            }

            OnAvatarPresentationChanged(sender, e);
        }

        private void EnableShadowOffCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (ShadowOffMeshTextBox != null)
            {
                ShadowOffMeshTextBox.IsEnabled = false;
            }

            OnAvatarPresentationChanged(sender, e);
        }

        private void OnAvatarPresentationChanged(object sender, RoutedEventArgs e)
        {
            if (_isInitialized)
            {
                SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private void AvatarNameTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isInitialized || _currentAvatarIndex < 0)
            {
                return;
            }

            if (_avatarNameChangeTimer != null)
            {
                _avatarNameChangeTimer.Stop();
                _avatarNameChangeTimer.Start();
            }
        }

        private void AvatarNameChangeTimer_Tick(object? sender, EventArgs e)
        {
            if (_avatarNameChangeTimer != null)
            {
                _avatarNameChangeTimer.Stop();
            }

            if (!_isInitialized || _currentAvatarIndex < 0 || _currentAvatarIndex >= AppSettings.Instance.AvatarList.Count)
            {
                return;
            }

            var newName = AvatarNameTextBox.Text;
            if (string.IsNullOrWhiteSpace(newName))
            {
                return;
            }

            var currentSelectedIndex = _currentAvatarIndex;
            AppSettings.Instance.AvatarList[_currentAvatarIndex].modelName = newName;

            AvatarSelectComboBox.SelectionChanged -= AvatarSelectComboBox_SelectionChanged;
            AvatarSelectComboBox.ItemsSource = null;
            AvatarSelectComboBox.ItemsSource = AppSettings.Instance.AvatarList;
            AvatarSelectComboBox.SelectedIndex = currentSelectedIndex;
            AvatarSelectComboBox.SelectionChanged += AvatarSelectComboBox_SelectionChanged;

            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        public void RefreshAvatarList()
        {
            AvatarSelectComboBox.SelectionChanged -= AvatarSelectComboBox_SelectionChanged;
            try
            {
                AvatarSelectComboBox.ItemsSource = null;
                AvatarSelectComboBox.ItemsSource = AppSettings.Instance.AvatarList;

                if (AppSettings.Instance.AvatarList.Count > 0)
                {
                    int indexToSelect = Math.Min(_currentAvatarIndex, AppSettings.Instance.AvatarList.Count - 1);
                    if (indexToSelect < 0)
                    {
                        indexToSelect = 0;
                    }

                    _currentAvatarIndex = indexToSelect;
                    AvatarSelectComboBox.SelectedIndex = indexToSelect;
                    UpdateAvatarUI();
                }
                else
                {
                    _currentAvatarIndex = -1;
                    AvatarNameTextBox.Text = string.Empty;
                    VRMFilePathTextBox.Text = string.Empty;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"アバターリスト復元エラー: {ex.Message}");
            }
            finally
            {
                AvatarSelectComboBox.SelectionChanged += AvatarSelectComboBox_SelectionChanged;
            }
        }
    }
}
