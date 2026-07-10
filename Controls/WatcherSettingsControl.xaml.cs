using CocoroConsole.Models.OtomeKairoApi;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace CocoroConsole.Controls
{
    public partial class WatcherSettingsControl : UserControl
    {
        private sealed class CameraWatcherEditorItem
        {
            public string? VisionSourceId { get; set; }
            public string CameraDisplayName { get; set; } = string.Empty;
            public bool Enabled { get; set; }
            public string WatcherId { get; set; } = string.Empty;
            public string Kind { get; set; } = "tapo_c220_motion";
            public double PollIntervalSeconds { get; set; } = 60;
            public double MinWakeIntervalSeconds { get; set; } = 60;
            public double MotionRatioThreshold { get; set; } = 0.03;
            public int PixelDiffThreshold { get; set; } = 25;
            public int ResizeWidth { get; set; } = 320;
            public string IdentityDisplay => $"{VisionSourceId} / {WatcherId}";
        }

        private readonly List<CameraWatcherEditorItem> _cameraWatchers = new();
        private bool _isInitializing;
        private int _currentWatcherIndex = -1;

        public event EventHandler? SettingsChanged;

        public WatcherSettingsControl()
        {
            InitializeComponent();
        }

        public void LoadCameraSources(OtomeKairoCameraSourcesEditorState? editorState)
        {
            _isInitializing = true;
            try
            {
                _cameraWatchers.Clear();
                _currentWatcherIndex = -1;
                RefreshCameraWatchersListBox();

                foreach (var cameraSource in editorState?.CameraSources ?? Enumerable.Empty<OtomeKairoCameraSourceDefinition>())
                {
                    _cameraWatchers.Add(ToEditorItem(cameraSource));
                }

                if (_cameraWatchers.Count == 0)
                {
                    ClearWatcherUi();
                    UpdateWatcherEditorEnabled();
                    return;
                }

                _currentWatcherIndex = 0;
                RefreshCameraWatchersListBox();
                CameraWatchersListBox.SelectedIndex = _currentWatcherIndex;
                LoadWatcherToUi(_cameraWatchers[_currentWatcherIndex]);
                UpdateWatcherEditorEnabled();
            }
            finally
            {
                _isInitializing = false;
            }
        }

        public void ApplyWatcherSettingsTo(OtomeKairoCameraSourcesEditorState editorState)
        {
            SyncCurrentWatcherFromUi();

            foreach (var cameraSource in editorState.CameraSources)
            {
                cameraSource.VisionSourceId = WatcherDefaults.BuildVisionSourceId(cameraSource.DisplayName);
                var item = FindMatchingWatcher(cameraSource);
                cameraSource.Watcher = item == null
                    ? BuildCameraWatcher(cameraSource.Watcher, cameraSource.VisionSourceId)
                    : ToDefinition(item);
            }
        }

        private void CameraWatchersListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing)
            {
                return;
            }

            SyncCurrentWatcherFromUi();

            var selectedIndex = CameraWatchersListBox.SelectedIndex;
            if (selectedIndex < 0 || selectedIndex >= _cameraWatchers.Count)
            {
                return;
            }

            _isInitializing = true;
            try
            {
                _currentWatcherIndex = selectedIndex;
                LoadWatcherToUi(_cameraWatchers[selectedIndex]);
                UpdateWatcherEditorEnabled();
            }
            finally
            {
                _isInitializing = false;
            }

            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void WatcherEnabledCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing)
            {
                return;
            }

            if (sender is CheckBox { Tag: CameraWatcherEditorItem item })
            {
                item.Enabled = ((CheckBox)sender).IsChecked ?? false;
            }

            RefreshCameraWatchersListBox();
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void OnWatcherTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isInitializing)
            {
                return;
            }

            SyncCurrentWatcherFromUi();
            RefreshCameraWatchersListBox();
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void SyncCurrentWatcherFromUi()
        {
            if (_currentWatcherIndex < 0 || _currentWatcherIndex >= _cameraWatchers.Count)
            {
                return;
            }

            var current = _cameraWatchers[_currentWatcherIndex];
            current.WatcherId = WatcherDefaults.BuildDefaultWatcherId(current.VisionSourceId);
            current.Kind = "tapo_c220_motion";
            current.PollIntervalSeconds = ParseDoubleOrDefault(PollIntervalTextBox.Text, 60);
            current.MinWakeIntervalSeconds = ParseDoubleOrDefault(MinWakeIntervalTextBox.Text, 60);
            current.MotionRatioThreshold = ParseDoubleOrDefault(MotionRatioThresholdTextBox.Text, 0.03);
            current.PixelDiffThreshold = ParseIntOrDefault(PixelDiffThresholdTextBox.Text, 25);
            current.ResizeWidth = ParseIntOrDefault(ResizeWidthTextBox.Text, 320);
        }

        private void LoadWatcherToUi(CameraWatcherEditorItem item)
        {
            WatcherDisplayNameTextBox.Text = item.CameraDisplayName;
            WatcherVisionSourceIdTextBox.Text = item.VisionSourceId ?? string.Empty;
            WatcherIdTextBox.Text = item.WatcherId;
            WatcherKindTextBox.Text = item.Kind;
            PollIntervalTextBox.Text = item.PollIntervalSeconds.ToString(CultureInfo.InvariantCulture);
            MinWakeIntervalTextBox.Text = item.MinWakeIntervalSeconds.ToString(CultureInfo.InvariantCulture);
            MotionRatioThresholdTextBox.Text = item.MotionRatioThreshold.ToString(CultureInfo.InvariantCulture);
            PixelDiffThresholdTextBox.Text = item.PixelDiffThreshold.ToString(CultureInfo.InvariantCulture);
            ResizeWidthTextBox.Text = item.ResizeWidth.ToString(CultureInfo.InvariantCulture);
        }

        private void ClearWatcherUi()
        {
            WatcherDisplayNameTextBox.Text = string.Empty;
            WatcherVisionSourceIdTextBox.Text = string.Empty;
            WatcherIdTextBox.Text = string.Empty;
            WatcherKindTextBox.Text = "tapo_c220_motion";
            PollIntervalTextBox.Text = "60";
            MinWakeIntervalTextBox.Text = "60";
            MotionRatioThresholdTextBox.Text = "0.03";
            PixelDiffThresholdTextBox.Text = "25";
            ResizeWidthTextBox.Text = "320";
        }

        private void UpdateWatcherEditorEnabled()
        {
            WatcherEditorPanel.IsEnabled = _currentWatcherIndex >= 0 && _currentWatcherIndex < _cameraWatchers.Count;
        }

        private void RefreshCameraWatchersListBox()
        {
            var currentIndex = _currentWatcherIndex;
            var wasInitializing = _isInitializing;
            _isInitializing = true;
            try
            {
                CameraWatchersListBox.SelectionChanged -= CameraWatchersListBox_SelectionChanged;
                CameraWatchersListBox.ItemsSource = null;
                CameraWatchersListBox.ItemsSource = _cameraWatchers;
                CameraWatchersListBox.SelectedIndex = currentIndex >= 0 && currentIndex < _cameraWatchers.Count ? currentIndex : -1;
                CameraWatchersListBox.SelectionChanged += CameraWatchersListBox_SelectionChanged;
            }
            finally
            {
                _isInitializing = wasInitializing;
            }
        }

        private CameraWatcherEditorItem? FindMatchingWatcher(OtomeKairoCameraSourceDefinition cameraSource)
        {
            if (!string.IsNullOrWhiteSpace(cameraSource.VisionSourceId))
            {
                var bySourceId = _cameraWatchers.FirstOrDefault(item => string.Equals(item.VisionSourceId, cameraSource.VisionSourceId, StringComparison.Ordinal));
                if (bySourceId != null)
                {
                    return bySourceId;
                }
            }

            return null;
        }

        private static CameraWatcherEditorItem ToEditorItem(OtomeKairoCameraSourceDefinition cameraSource)
        {
            var visionSourceId = WatcherDefaults.BuildVisionSourceId(cameraSource.DisplayName);
            var watcher = BuildCameraWatcher(cameraSource.Watcher, visionSourceId);
            return new CameraWatcherEditorItem
            {
                VisionSourceId = visionSourceId,
                CameraDisplayName = string.IsNullOrWhiteSpace(cameraSource.DisplayName) ? "Camera" : cameraSource.DisplayName.Trim(),
                Enabled = watcher.Enabled,
                WatcherId = watcher.WatcherId,
                Kind = watcher.Kind,
                PollIntervalSeconds = watcher.PollIntervalSeconds,
                MinWakeIntervalSeconds = watcher.MinWakeIntervalSeconds,
                MotionRatioThreshold = watcher.MotionRatioThreshold,
                PixelDiffThreshold = watcher.PixelDiffThreshold,
                ResizeWidth = watcher.ResizeWidth,
            };
        }

        private static OtomeKairoCameraWatcherDefinition ToDefinition(CameraWatcherEditorItem item)
        {
            return new OtomeKairoCameraWatcherDefinition
            {
                Enabled = item.Enabled,
                WatcherId = WatcherDefaults.BuildDefaultWatcherId(item.VisionSourceId),
                Kind = "tapo_c220_motion",
                PollIntervalSeconds = item.PollIntervalSeconds,
                MinWakeIntervalSeconds = item.MinWakeIntervalSeconds,
                MotionRatioThreshold = item.MotionRatioThreshold,
                PixelDiffThreshold = item.PixelDiffThreshold,
                ResizeWidth = item.ResizeWidth,
            };
        }

        private static OtomeKairoCameraWatcherDefinition BuildCameraWatcher(OtomeKairoCameraWatcherDefinition? watcher, string? visionSourceId)
        {
            return new OtomeKairoCameraWatcherDefinition
            {
                Enabled = watcher?.Enabled == true,
                WatcherId = WatcherDefaults.BuildDefaultWatcherId(visionSourceId),
                Kind = "tapo_c220_motion",
                PollIntervalSeconds = watcher?.PollIntervalSeconds > 0 ? watcher.PollIntervalSeconds : 60,
                MinWakeIntervalSeconds = watcher?.MinWakeIntervalSeconds > 0 ? watcher.MinWakeIntervalSeconds : 60,
                MotionRatioThreshold = watcher?.MotionRatioThreshold > 0 ? watcher.MotionRatioThreshold : 0.03,
                PixelDiffThreshold = watcher?.PixelDiffThreshold > 0 ? watcher.PixelDiffThreshold : 25,
                ResizeWidth = watcher?.ResizeWidth > 0 ? watcher.ResizeWidth : 320,
            };
        }

        private static double ParseDoubleOrDefault(string? text, double fallback)
        {
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value > 0
                ? value
                : fallback;
        }

        private static int ParseIntOrDefault(string? text, int fallback)
        {
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0
                ? value
                : fallback;
        }
    }
}
