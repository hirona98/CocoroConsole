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
        private const string DefaultMcpConnectorKind = "mcp_client";
        private const string DefaultMcpClientId = "mcp-client-connector-main";
        private const string DefaultMcpTransport = "stdio";

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

        private sealed class McpEnvEditorItem
        {
            public string Key { get; set; } = string.Empty;
            public string Value { get; set; } = string.Empty;
        }

        private sealed class McpServerEditorItem
        {
            public string McpServerId { get; set; } = string.Empty;
            public string ConnectorKind { get; set; } = DefaultMcpConnectorKind;
            public string ClientId { get; set; } = DefaultMcpClientId;
            public bool Enabled { get; set; }
            public string Transport { get; set; } = DefaultMcpTransport;
            public string Command { get; set; } = string.Empty;
            public string ArgsText { get; set; } = string.Empty;
            public string Cwd { get; set; } = string.Empty;
            public List<McpEnvEditorItem> Env { get; set; } = new();
            public string CommandDisplay => string.IsNullOrWhiteSpace(Command) ? "command未設定" : Command.Trim();
        }

        private readonly List<CameraSourceEditorItem> _cameraSources = new();
        private readonly List<McpServerEditorItem> _mcpServers = new();
        private bool _isInitializing;
        private int _currentCameraSourceIndex = -1;
        private int _currentMcpServerIndex = -1;

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

        public void LoadMcpServers(OtomeKairoMcpServersEditorState? editorState)
        {
            _isInitializing = true;
            try
            {
                _mcpServers.Clear();
                _currentMcpServerIndex = -1;
                RefreshMcpServerListBox();

                if (editorState?.McpServers == null || editorState.McpServers.Count == 0)
                {
                    _currentMcpServerIndex = -1;
                    ClearMcpServerUi();
                    UpdateMcpEditorEnabled();
                    return;
                }

                foreach (var mcpServer in editorState.McpServers)
                {
                    _mcpServers.Add(ToEditorItem(mcpServer));
                }

                _currentMcpServerIndex = 0;
                RefreshMcpServerListBox();
                McpServersListBox.SelectedIndex = _currentMcpServerIndex;
                LoadMcpServerToUi(_mcpServers[_currentMcpServerIndex]);
                UpdateMcpEditorEnabled();
            }
            finally
            {
                _isInitializing = false;
            }
        }

        public OtomeKairoMcpServersEditorState GetMcpServersEditorState()
        {
            SyncCurrentMcpServerFromUi();
            ValidateMcpServers();
            return new OtomeKairoMcpServersEditorState
            {
                McpServers = _mcpServers.Select(ToDefinition).ToList(),
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

        private void AddMcpServerButton_Click(object sender, RoutedEventArgs e)
        {
            SyncCurrentMcpServerFromUi();

            var item = new McpServerEditorItem
            {
                Enabled = true,
                McpServerId = GenerateUniqueMcpServerId(),
                Command = "npx",
                ArgsText = "-y",
            };

            _isInitializing = true;
            try
            {
                _mcpServers.Add(item);
                _currentMcpServerIndex = _mcpServers.Count - 1;
                RefreshMcpServerListBox();
                McpServersListBox.SelectedIndex = _currentMcpServerIndex;
                LoadMcpServerToUi(item);
                UpdateMcpEditorEnabled();
            }
            finally
            {
                _isInitializing = false;
            }

            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void DeleteMcpServerButton_Click(object sender, RoutedEventArgs e)
        {
            var deleteIndex = ResolveMcpServerIndexFromSender(sender);
            if (deleteIndex < 0 || deleteIndex >= _mcpServers.Count)
            {
                MessageBox.Show("削除するMCP serverを選択してください。", "情報", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _isInitializing = true;
            try
            {
                _mcpServers.RemoveAt(deleteIndex);
                if (_mcpServers.Count == 0)
                {
                    _currentMcpServerIndex = -1;
                    RefreshMcpServerListBox();
                    ClearMcpServerUi();
                    UpdateMcpEditorEnabled();
                }
                else
                {
                    _currentMcpServerIndex = Math.Min(deleteIndex, _mcpServers.Count - 1);
                    RefreshMcpServerListBox();
                    McpServersListBox.SelectedIndex = _currentMcpServerIndex;
                    LoadMcpServerToUi(_mcpServers[_currentMcpServerIndex]);
                    UpdateMcpEditorEnabled();
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

        private void McpServersListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing)
            {
                return;
            }

            SyncCurrentMcpServerFromUi();

            var selectedIndex = McpServersListBox.SelectedIndex;
            if (selectedIndex < 0 || selectedIndex >= _mcpServers.Count)
            {
                return;
            }

            _isInitializing = true;
            try
            {
                _currentMcpServerIndex = selectedIndex;
                LoadMcpServerToUi(_mcpServers[selectedIndex]);
                UpdateMcpEditorEnabled();
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

        private void OnMcpTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isInitializing)
            {
                return;
            }

            if (sender == McpCommandTextBox || sender == McpServerIdTextBox)
            {
                SyncCurrentMcpServerFromUi();
                RefreshMcpServerListBox();
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

        private void McpServerEnabledCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing)
            {
                return;
            }

            if (sender is CheckBox { Tag: McpServerEditorItem item })
            {
                item.Enabled = ((CheckBox)sender).IsChecked ?? false;
            }

            RefreshMcpServerListBox();
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void AddMcpEnvButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentMcpServerIndex < 0 || _currentMcpServerIndex >= _mcpServers.Count)
            {
                return;
            }

            SyncCurrentMcpServerFromUi();
            _mcpServers[_currentMcpServerIndex].Env.Add(new McpEnvEditorItem());
            RefreshMcpEnvListBox(_mcpServers[_currentMcpServerIndex]);
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void DeleteMcpEnvButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentMcpServerIndex < 0 || _currentMcpServerIndex >= _mcpServers.Count)
            {
                return;
            }

            if (sender is Button { Tag: McpEnvEditorItem item })
            {
                SyncCurrentMcpServerFromUi();
                _mcpServers[_currentMcpServerIndex].Env.Remove(item);
                RefreshMcpEnvListBox(_mcpServers[_currentMcpServerIndex]);
                SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
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

        private void SyncCurrentMcpServerFromUi()
        {
            if (_currentMcpServerIndex < 0 || _currentMcpServerIndex >= _mcpServers.Count)
            {
                return;
            }

            var current = _mcpServers[_currentMcpServerIndex];
            current.McpServerId = McpServerIdTextBox.Text;
            current.ConnectorKind = DefaultMcpConnectorKind;
            current.ClientId = McpClientIdTextBox.Text;
            current.Transport = NormalizeMcpTransport(McpTransportTextBox.Text);
            current.Command = McpCommandTextBox.Text;
            current.ArgsText = McpArgsTextBox.Text;
            current.Cwd = McpCwdTextBox.Text;
            current.Env = McpEnvListBox.Items.Cast<McpEnvEditorItem>().ToList();
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

        private void LoadMcpServerToUi(McpServerEditorItem item)
        {
            McpServerIdTextBox.Text = item.McpServerId;
            McpCommandTextBox.Text = item.Command;
            McpArgsTextBox.Text = item.ArgsText;
            McpCwdTextBox.Text = item.Cwd;
            McpClientIdTextBox.Text = NormalizeMcpClientId(item.ClientId);
            McpTransportTextBox.Text = NormalizeMcpTransport(item.Transport);
            RefreshMcpEnvListBox(item);
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

        private void ClearMcpServerUi()
        {
            McpServerIdTextBox.Text = string.Empty;
            McpCommandTextBox.Text = string.Empty;
            McpArgsTextBox.Text = string.Empty;
            McpCwdTextBox.Text = string.Empty;
            McpClientIdTextBox.Text = DefaultMcpClientId;
            McpTransportTextBox.Text = DefaultMcpTransport;
            McpEnvListBox.ItemsSource = null;
        }

        private void UpdateCameraEditorEnabled()
        {
            CameraEditorPanel.IsEnabled = _currentCameraSourceIndex >= 0 && _currentCameraSourceIndex < _cameraSources.Count;
        }

        private void UpdateMcpEditorEnabled()
        {
            McpEditorPanel.IsEnabled = _currentMcpServerIndex >= 0 && _currentMcpServerIndex < _mcpServers.Count;
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

        private void RefreshMcpServerListBox()
        {
            var currentIndex = _currentMcpServerIndex;
            var wasInitializing = _isInitializing;
            _isInitializing = true;
            try
            {
                McpServersListBox.SelectionChanged -= McpServersListBox_SelectionChanged;
                McpServersListBox.ItemsSource = null;
                McpServersListBox.ItemsSource = _mcpServers;
                McpServersListBox.SelectedIndex = currentIndex >= 0 && currentIndex < _mcpServers.Count ? currentIndex : -1;
                McpServersListBox.SelectionChanged += McpServersListBox_SelectionChanged;
            }
            finally
            {
                _isInitializing = wasInitializing;
            }
        }

        private void RefreshMcpEnvListBox(McpServerEditorItem item)
        {
            McpEnvListBox.ItemsSource = null;
            McpEnvListBox.ItemsSource = item.Env;
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

        private static McpServerEditorItem ToEditorItem(OtomeKairoMcpServerDefinition mcpServer)
        {
            return new McpServerEditorItem
            {
                McpServerId = mcpServer.McpServerId,
                ConnectorKind = NormalizeMcpConnectorKind(mcpServer.ConnectorKind),
                ClientId = NormalizeMcpClientId(mcpServer.ClientId),
                Enabled = mcpServer.Enabled,
                Transport = NormalizeMcpTransport(mcpServer.Transport),
                Command = mcpServer.Command,
                ArgsText = BuildArgsText(mcpServer.Args),
                Cwd = mcpServer.Cwd ?? string.Empty,
                Env = mcpServer.Env
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => new McpEnvEditorItem
                    {
                        Key = pair.Key,
                        Value = pair.Value,
                    })
                    .ToList(),
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

        private static OtomeKairoMcpServerDefinition ToDefinition(McpServerEditorItem item)
        {
            return new OtomeKairoMcpServerDefinition
            {
                McpServerId = item.McpServerId.Trim(),
                ConnectorKind = NormalizeMcpConnectorKind(item.ConnectorKind),
                ClientId = NormalizeMcpClientId(item.ClientId),
                Enabled = item.Enabled,
                Transport = NormalizeMcpTransport(item.Transport),
                Command = item.Command.Trim(),
                Args = ParseArgsText(item.ArgsText),
                Cwd = string.IsNullOrWhiteSpace(item.Cwd) ? null : item.Cwd.Trim(),
                Env = item.Env.ToDictionary(
                    env => env.Key.Trim(),
                    env => env.Value ?? string.Empty,
                    StringComparer.Ordinal),
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

        private int ResolveMcpServerIndexFromSender(object sender)
        {
            if (sender is Button { Tag: McpServerEditorItem item })
            {
                return _mcpServers.IndexOf(item);
            }

            return _currentMcpServerIndex;
        }

        private static string NormalizeConnectorKind(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? DefaultConnectorKind : value.Trim();
        }

        private static string NormalizeClientId(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? DefaultClientId : value.Trim();
        }

        private static string NormalizeMcpConnectorKind(string? value)
        {
            return DefaultMcpConnectorKind;
        }

        private static string NormalizeMcpClientId(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? DefaultMcpClientId : value.Trim();
        }

        private static string NormalizeMcpTransport(string? value)
        {
            return DefaultMcpTransport;
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

        private string GenerateUniqueMcpServerId()
        {
            var existingIds = new HashSet<string>(_mcpServers.Select(server => server.McpServerId), StringComparer.Ordinal);
            var counter = 1;
            while (true)
            {
                var id = $"mcp_server:custom:{counter}";
                if (!existingIds.Contains(id))
                {
                    return id;
                }

                counter += 1;
            }
        }

        private static List<string> ParseArgsText(string? text)
        {
            return (text ?? string.Empty)
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Split('\n')
                .Select(arg => arg.Trim())
                .Where(arg => arg.Length > 0)
                .ToList();
        }

        private static string BuildArgsText(IEnumerable<string>? args)
        {
            return string.Join(Environment.NewLine, args ?? Array.Empty<string>());
        }

        private void ValidateMcpServers()
        {
            var serverIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (var server in _mcpServers)
            {
                var id = server.McpServerId?.Trim() ?? string.Empty;
                if (id.Length == 0)
                {
                    throw new InvalidOperationException("MCP server IDを入力してください。");
                }

                if (!id.StartsWith("mcp_server:", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"MCP server IDは mcp_server: で始めてください: {id}");
                }

                if (!serverIds.Add(id))
                {
                    throw new InvalidOperationException($"MCP server IDが重複しています: {id}");
                }

                if (string.IsNullOrWhiteSpace(server.Command))
                {
                    throw new InvalidOperationException($"MCP serverのcommandを入力してください: {id}");
                }

                var envKeys = new HashSet<string>(StringComparer.Ordinal);
                foreach (var env in server.Env)
                {
                    var key = env.Key?.Trim() ?? string.Empty;
                    if (key.Length == 0)
                    {
                        throw new InvalidOperationException($"MCP serverのenv keyを入力してください: {id}");
                    }

                    if (!envKeys.Add(key))
                    {
                        throw new InvalidOperationException($"MCP serverのenv keyが重複しています: {id} / {key}");
                    }
                }
            }
        }
    }
}
