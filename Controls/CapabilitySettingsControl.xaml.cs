using CocoroConsole.Models.OtomeKairoApi;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace CocoroConsole.Controls
{
    public partial class CapabilitySettingsControl : UserControl
    {
        private const string DefaultConnectorKind = "tapo_c220";
        private const string DefaultClientId = "tapo-c220-connector-main";

        private sealed class CameraSourceEditorItem
        {
            public string? VisionSourceId { get; set; }
            public string ConnectorKind { get; set; } = DefaultConnectorKind;
            public string ClientId { get; set; } = DefaultClientId;
            public bool Enabled { get; set; }
            public string Label { get; set; } = string.Empty;
            public string Host { get; set; } = string.Empty;
            public string CameraUsername { get; set; } = string.Empty;
            public string CameraPassword { get; set; } = string.Empty;
            public string DisplayName => string.IsNullOrWhiteSpace(Label) ? "名称なし" : Label.Trim();
            public string HostDisplay => string.IsNullOrWhiteSpace(Host) ? "IP未設定" : Host.Trim();
        }

        private readonly List<CameraSourceEditorItem> _cameraSources = new();
        private bool _isInitializing;
        private int _currentCameraSourceIndex = -1;

        public event EventHandler? SettingsChanged;

        public CapabilitySettingsControl()
        {
            InitializeComponent();
        }

        public void LoadCameraSources(OtomeKairoCameraSourcesEditorState? editorState)
        {
            _isInitializing = true;
            try
            {
                _cameraSources.Clear();
                _currentCameraSourceIndex = -1;
                RefreshCameraSourceListBox();

                if (editorState?.CameraSources == null || editorState.CameraSources.Count == 0)
                {
                    _currentCameraSourceIndex = -1;
                    ClearCameraSourceUi();
                    UpdateCameraEditorEnabled();
                    return;
                }

                foreach (var cameraSource in editorState.CameraSources)
                {
                    var item = ToEditorItem(cameraSource);
                    _cameraSources.Add(item);
                }

                _currentCameraSourceIndex = 0;
                RefreshCameraSourceListBox();
                CameraSourcesListBox.SelectedIndex = _currentCameraSourceIndex;
                LoadCameraSourceToUi(_cameraSources[_currentCameraSourceIndex]);
                UpdateCameraEditorEnabled();
            }
            finally
            {
                _isInitializing = false;
            }
        }

        public OtomeKairoCameraSourcesEditorState GetCameraSourcesEditorState()
        {
            SyncCurrentCameraSourceFromUi();
            return new OtomeKairoCameraSourcesEditorState
            {
                CameraSources = _cameraSources.Select(ToDefinition).ToList(),
            };
        }

        private void AddCameraSourceButton_Click(object sender, RoutedEventArgs e)
        {
            SyncCurrentCameraSourceFromUi();

            var item = new CameraSourceEditorItem
            {
                Label = GenerateUniqueName(_cameraSources.Select(source => source.Label), "新しいカメラ"),
            };

            _isInitializing = true;
            try
            {
                _cameraSources.Add(item);
                _currentCameraSourceIndex = _cameraSources.Count - 1;
                RefreshCameraSourceListBox();
                CameraSourcesListBox.SelectedIndex = _currentCameraSourceIndex;
                LoadCameraSourceToUi(item);
                UpdateCameraEditorEnabled();
            }
            finally
            {
                _isInitializing = false;
            }

            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void DeleteCameraSourceButton_Click(object sender, RoutedEventArgs e)
        {
            var deleteIndex = ResolveCameraSourceIndexFromSender(sender);
            if (deleteIndex < 0 || deleteIndex >= _cameraSources.Count)
            {
                MessageBox.Show("削除するカメラを選択してください。", "情報", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _isInitializing = true;
            try
            {
                _cameraSources.RemoveAt(deleteIndex);
                if (_cameraSources.Count == 0)
                {
                    _currentCameraSourceIndex = -1;
                    RefreshCameraSourceListBox();
                    ClearCameraSourceUi();
                    UpdateCameraEditorEnabled();
                }
                else
                {
                    _currentCameraSourceIndex = Math.Min(deleteIndex, _cameraSources.Count - 1);
                    RefreshCameraSourceListBox();
                    CameraSourcesListBox.SelectedIndex = _currentCameraSourceIndex;
                    LoadCameraSourceToUi(_cameraSources[_currentCameraSourceIndex]);
                    UpdateCameraEditorEnabled();
                }
            }
            finally
            {
                _isInitializing = false;
            }

            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void CameraSourcesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing)
            {
                return;
            }

            SyncCurrentCameraSourceFromUi();

            var selectedIndex = CameraSourcesListBox.SelectedIndex;
            if (selectedIndex < 0 || selectedIndex >= _cameraSources.Count)
            {
                return;
            }

            _isInitializing = true;
            try
            {
                _currentCameraSourceIndex = selectedIndex;
                LoadCameraSourceToUi(_cameraSources[selectedIndex]);
                UpdateCameraEditorEnabled();
            }
            finally
            {
                _isInitializing = false;
            }

            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void OnTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isInitializing)
            {
                return;
            }

            if (sender == CameraLabelTextBox || sender == CameraHostTextBox)
            {
                SyncCurrentCameraSourceFromUi();
                RefreshCameraSourceListBox();
            }

            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void CameraSourceEnabledCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing)
            {
                return;
            }

            if (sender is CheckBox { Tag: CameraSourceEditorItem item })
            {
                item.Enabled = ((CheckBox)sender).IsChecked ?? false;
            }

            RefreshCameraSourceListBox();
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void CameraPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (_isInitializing)
            {
                return;
            }

            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void SyncCurrentCameraSourceFromUi()
        {
            if (_currentCameraSourceIndex < 0 || _currentCameraSourceIndex >= _cameraSources.Count)
            {
                return;
            }

            var current = _cameraSources[_currentCameraSourceIndex];
            current.Label = CameraLabelTextBox.Text;
            current.Host = CameraHostTextBox.Text;
            current.CameraUsername = CameraUsernameTextBox.Text;
            current.CameraPassword = CameraPasswordBox.Password;
        }

        private void LoadCameraSourceToUi(CameraSourceEditorItem item)
        {
            CameraLabelTextBox.Text = item.Label;
            CameraHostTextBox.Text = item.Host;
            CameraUsernameTextBox.Text = item.CameraUsername;
            CameraPasswordBox.Password = item.CameraPassword;
            CameraConnectorKindTextBox.Text = NormalizeConnectorKind(item.ConnectorKind);
            CameraClientIdTextBox.Text = NormalizeClientId(item.ClientId);
            CameraVisionSourceIdTextBox.Text = item.VisionSourceId ?? string.Empty;
        }

        private void ClearCameraSourceUi()
        {
            CameraLabelTextBox.Text = string.Empty;
            CameraHostTextBox.Text = string.Empty;
            CameraUsernameTextBox.Text = string.Empty;
            CameraPasswordBox.Password = string.Empty;
            CameraConnectorKindTextBox.Text = DefaultConnectorKind;
            CameraClientIdTextBox.Text = DefaultClientId;
            CameraVisionSourceIdTextBox.Text = string.Empty;
        }

        private void UpdateCameraEditorEnabled()
        {
            CameraEditorPanel.IsEnabled = _currentCameraSourceIndex >= 0 && _currentCameraSourceIndex < _cameraSources.Count;
        }

        private void RefreshCameraSourceListBox()
        {
            var currentIndex = _currentCameraSourceIndex;
            var wasInitializing = _isInitializing;
            _isInitializing = true;
            try
            {
                CameraSourcesListBox.SelectionChanged -= CameraSourcesListBox_SelectionChanged;
                CameraSourcesListBox.ItemsSource = null;
                CameraSourcesListBox.ItemsSource = _cameraSources;
                CameraSourcesListBox.SelectedIndex = currentIndex >= 0 && currentIndex < _cameraSources.Count ? currentIndex : -1;
                CameraSourcesListBox.SelectionChanged += CameraSourcesListBox_SelectionChanged;
            }
            finally
            {
                _isInitializing = wasInitializing;
            }
        }

        private static CameraSourceEditorItem ToEditorItem(OtomeKairoCameraSourceDefinition cameraSource)
        {
            return new CameraSourceEditorItem
            {
                VisionSourceId = string.IsNullOrWhiteSpace(cameraSource.VisionSourceId) ? null : cameraSource.VisionSourceId,
                ConnectorKind = NormalizeConnectorKind(cameraSource.ConnectorKind),
                ClientId = NormalizeClientId(cameraSource.ClientId),
                Enabled = cameraSource.Enabled,
                Label = cameraSource.Label,
                Host = cameraSource.Connection?.Host ?? string.Empty,
                CameraUsername = cameraSource.Connection?.CameraUsername ?? string.Empty,
                CameraPassword = cameraSource.Connection?.CameraPassword ?? string.Empty,
            };
        }

        private static OtomeKairoCameraSourceDefinition ToDefinition(CameraSourceEditorItem item)
        {
            return new OtomeKairoCameraSourceDefinition
            {
                VisionSourceId = string.IsNullOrWhiteSpace(item.VisionSourceId) ? null : item.VisionSourceId.Trim(),
                ConnectorKind = NormalizeConnectorKind(item.ConnectorKind),
                ClientId = NormalizeClientId(item.ClientId),
                Enabled = item.Enabled,
                Label = string.IsNullOrWhiteSpace(item.Label) ? "Camera" : item.Label.Trim(),
                Connection = new OtomeKairoCameraSourceConnection
                {
                    Host = item.Host?.Trim() ?? string.Empty,
                    CameraUsername = item.CameraUsername ?? string.Empty,
                    CameraPassword = item.CameraPassword ?? string.Empty,
                },
            };
        }

        private int ResolveCameraSourceIndexFromSender(object sender)
        {
            if (sender is Button { Tag: CameraSourceEditorItem item })
            {
                return _cameraSources.IndexOf(item);
            }

            return _currentCameraSourceIndex;
        }

        private static string NormalizeConnectorKind(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? DefaultConnectorKind : value.Trim();
        }

        private static string NormalizeClientId(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? DefaultClientId : value.Trim();
        }

        private static string GenerateUniqueName(IEnumerable<string> existingNames, string baseName)
        {
            var existing = new HashSet<string>(existingNames.Where(name => !string.IsNullOrWhiteSpace(name)));
            var name = baseName;
            var counter = 1;
            while (existing.Contains(name))
            {
                counter += 1;
                name = $"{baseName} {counter}";
            }
            return name;
        }
    }
}
