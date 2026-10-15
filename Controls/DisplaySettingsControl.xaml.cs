using CocoroConsole.Models.OtomeKairoApi;
using CocoroConsole.Services;
using CocoroConsole.Communication;
using CocoroConsole.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;

namespace CocoroConsole.Controls
{
    public partial class DisplaySettingsControl : UserControl
    {
        // 設定のスナップショット
        private Dictionary<string, object> _displaySettings = new();

        // キーボードフック用（Win/Alt検出とキャプチャ）
        private HwndSource? _source;
        private bool _isCapturingKey = false;
        private bool _isWinKeyPressed = false; // Windowsキーの状態
        private bool _isAltKeyPressed = false; // Altキーの状態
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;
        private const int VK_LWIN = 0x5B;  // 左Windowsキー
        private const int VK_RWIN = 0x5C;  // 右Windowsキー
        private const int VK_LALT = 0xA4;  // 左Altキー
        private const int VK_RALT = 0xA5;  // 右Altキー

        // グローバルキーボードフック用
        private const int WH_KEYBOARD_LL = 13;
        private LowLevelKeyboardProc _proc;
        private IntPtr _hookID = IntPtr.Zero;

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        public DisplaySettingsControl()
        {
            InitializeComponent();
            _proc = HookCallback;
            _hookID = SetHook(_proc);

            // UIイベント
            CaptureKeyButton.Click += CaptureKeyButton_Click;

            // 初期表示へ反映
            InitializeFromAppSettings();

            // Loaded/UnloadedでWndProcフックを管理
            Loaded += (s, e) =>
            {
                _source = PresentationSource.FromVisual(this) as HwndSource;
                _source?.AddHook(WndProc);
            };
            Unloaded += (s, e) =>
            {
                if (_source != null)
                {
                    _source.RemoveHook(WndProc);
                    _source = null;
                }
                if (_hookID != IntPtr.Zero)
                {
                    UnhookWindowsHookEx(_hookID);
                    _hookID = IntPtr.Zero;
                }
            };
        }

        public void SetCommunicationService(ICommunicationService? service)
        {
            if (service != null)
            {
                EscapePositionControl.SetCommunicationService(service);
            }
        }

        public void InitializeFromAppSettings()
        {
            var display = AppSettings.Instance.ConsoleClient.Display;
            var messageWindow = display.MessageWindow;
            RestoreWindowPositionCheckBox.IsChecked = display.RestoreWindowPosition;
            TopMostCheckBox.IsChecked = display.Topmost;
            EscapeCursorCheckBox.IsChecked = display.EscapeCursor;
            InputVirtualKeyCheckBox.IsChecked = display.TouchVirtualKeyEnabled;
            VirtualKeyStringTextBox.Text = display.VirtualKey;
            AutoMoveCheckBox.IsChecked = display.AutoMove;
            ShowMessageWindowCheckBox.IsChecked = display.ShowMessageWindow;

            MaxMessageCountTextBox.Text = messageWindow.MaxMessageCount.ToString();
            MaxTotalAvatarsTextBox.Text = messageWindow.MaxTotalCharacters.ToString();
            MinWindowSizeTextBox.Text = messageWindow.MinWindowSize.ToString();
            MaxWindowSizeTextBox.Text = messageWindow.MaxWindowSize.ToString();
            FontSizeTextBox.Text = messageWindow.FontSize.ToString();
            HorizontalOffsetTextBox.Text = messageWindow.HorizontalOffset.ToString();
            VerticalOffsetTextBox.Text = messageWindow.VerticalOffset.ToString();
            AmbientOcclusionCheckBox.IsChecked = display.AmbientOcclusionEnabled;

            foreach (ComboBoxItem item in MSAAComboBox.Items)
            {
                if (item.Tag != null &&
                    int.TryParse(item.Tag.ToString(), out int value) &&
                    value == display.MsaaLevel)
                {
                    MSAAComboBox.SelectedItem = item;
                    break;
                }
            }
            foreach (ComboBoxItem item in AvatarShadowComboBox.Items)
            {
                if (item.Tag != null &&
                    int.TryParse(item.Tag.ToString(), out int value) &&
                    value == display.AvatarShadowMode)
                {
                    AvatarShadowComboBox.SelectedItem = item;
                    break;
                }
            }
            foreach (ComboBoxItem item in AvatarShadowResolutionComboBox.Items)
            {
                if (item.Tag != null &&
                    int.TryParse(item.Tag.ToString(), out int value) &&
                    value == display.AvatarShadowResolution)
                {
                    AvatarShadowResolutionComboBox.SelectedItem = item;
                    break;
                }
            }
            foreach (ComboBoxItem item in BackgroundShadowComboBox.Items)
            {
                if (item.Tag != null &&
                    int.TryParse(item.Tag.ToString(), out int value) &&
                    value == display.BackgroundShadowMode)
                {
                    BackgroundShadowComboBox.SelectedItem = item;
                    break;
                }
            }
            foreach (ComboBoxItem item in BackgroundShadowResolutionComboBox.Items)
            {
                if (item.Tag != null &&
                    int.TryParse(item.Tag.ToString(), out int value) &&
                    value == display.BackgroundShadowResolution)
                {
                    BackgroundShadowResolutionComboBox.SelectedItem = item;
                    break;
                }
            }
            WindowSizeSlider.Value = display.AvatarWindowSize;
        }

        public void SaveToSnapshot()
        {
            _displaySettings["RestoreWindowPosition"] = RestoreWindowPositionCheckBox.IsChecked ?? false;
            _displaySettings["TopMost"] = TopMostCheckBox.IsChecked ?? false;
            _displaySettings["EscapeCursor"] = EscapeCursorCheckBox.IsChecked ?? false;

            // 移動先座標
            _displaySettings["EscapePositions"] = EscapePositionControl.GetEscapePositions();

            _displaySettings["InputVirtualKey"] = InputVirtualKeyCheckBox.IsChecked ?? false;
            _displaySettings["VirtualKeyString"] = VirtualKeyStringTextBox.Text;
            _displaySettings["AutoMove"] = AutoMoveCheckBox.IsChecked ?? false;
            _displaySettings["ShowMessageWindow"] = ShowMessageWindowCheckBox.IsChecked ?? false;

            // メッセージウィンドウ設定のスナップショット保存
            var messageWindowSettings = new Dictionary<string, object>
            {
                ["maxMessageCount"] = int.TryParse(MaxMessageCountTextBox.Text, out int maxCount) ? maxCount : 3,
                ["maxTotalAvatars"] = int.TryParse(MaxTotalAvatarsTextBox.Text, out int maxChars) ? maxChars : 300,
                ["minWindowSize"] = float.TryParse(MinWindowSizeTextBox.Text, out float minSize) ? minSize : 200f,
                ["maxWindowSize"] = float.TryParse(MaxWindowSizeTextBox.Text, out float maxSize) ? maxSize : 600f,
                ["fontSize"] = float.TryParse(FontSizeTextBox.Text, out float fontSize) ? fontSize : 14f,
                ["horizontalOffset"] = float.TryParse(HorizontalOffsetTextBox.Text, out float hOffset) ? hOffset : -0.2f,
                ["verticalOffset"] = float.TryParse(VerticalOffsetTextBox.Text, out float vOffset) ? vOffset : 0f
            };
            _displaySettings["MessageWindowSettings"] = messageWindowSettings;
            _displaySettings["IsEnableAmbientOcclusion"] = AmbientOcclusionCheckBox.IsChecked ?? false;

            int msaaLevel = 0;
            if (MSAAComboBox.SelectedItem is ComboBoxItem msaaItem && msaaItem.Tag != null)
                int.TryParse(msaaItem.Tag.ToString(), out msaaLevel);
            _displaySettings["MsaaLevel"] = msaaLevel;

            int charaShadow = 0;
            if (AvatarShadowComboBox.SelectedItem is ComboBoxItem charaShadowItem && charaShadowItem.Tag != null)
                int.TryParse(charaShadowItem.Tag.ToString(), out charaShadow);
            _displaySettings["AvatarShadow"] = charaShadow;

            int shadowRes = 0;
            if (AvatarShadowResolutionComboBox.SelectedItem is ComboBoxItem shadowResItem && shadowResItem.Tag != null)
                int.TryParse(shadowResItem.Tag.ToString(), out shadowRes);
            _displaySettings["AvatarShadowResolution"] = shadowRes;

            int backShadow = 0;
            if (BackgroundShadowComboBox.SelectedItem is ComboBoxItem backShadowItem && backShadowItem.Tag != null)
                int.TryParse(backShadowItem.Tag.ToString(), out backShadow);
            _displaySettings["BackgroundShadow"] = backShadow;

            int backShadowRes = 0;
            if (BackgroundShadowResolutionComboBox.SelectedItem is ComboBoxItem backShadowResItem && backShadowResItem.Tag != null)
                int.TryParse(backShadowResItem.Tag.ToString(), out backShadowRes);
            _displaySettings["BackgroundShadowResolution"] = backShadowRes;

            _displaySettings["WindowSize"] = WindowSizeSlider.Value;
        }

        public Dictionary<string, object> GetSnapshot()
        {
            return new Dictionary<string, object>(_displaySettings);
        }

        public void ApplySnapshotToAppSettings(Dictionary<string, object> snapshot)
        {
            var display = AppSettings.Instance.ConsoleClient.Display;
            display.RestoreWindowPosition = (bool)snapshot["RestoreWindowPosition"];
            display.Topmost = (bool)snapshot["TopMost"];
            display.EscapeCursor = (bool)snapshot["EscapeCursor"];
            if (snapshot.ContainsKey("EscapePositions"))
            {
                display.EscapePositions = (System.Collections.Generic.List<OtomeKairoConsoleEscapePosition>)snapshot["EscapePositions"];
                EscapePositionControl.SetEscapePositions(display.EscapePositions);
            }
            display.TouchVirtualKeyEnabled = (bool)snapshot["InputVirtualKey"];
            display.VirtualKey = (string)snapshot["VirtualKeyString"];
            display.AutoMove = (bool)snapshot["AutoMove"];
            display.ShowMessageWindow = (bool)snapshot["ShowMessageWindow"];

            if (snapshot.ContainsKey("MessageWindowSettings") && snapshot["MessageWindowSettings"] is Dictionary<string, object> msgSettings)
            {
                var messageWindow = display.MessageWindow;
                if (msgSettings.ContainsKey("maxMessageCount"))
                    messageWindow.MaxMessageCount = Convert.ToInt32(msgSettings["maxMessageCount"]);
                if (msgSettings.ContainsKey("maxTotalAvatars"))
                    messageWindow.MaxTotalCharacters = Convert.ToInt32(msgSettings["maxTotalAvatars"]);
                if (msgSettings.ContainsKey("minWindowSize"))
                    messageWindow.MinWindowSize = Convert.ToSingle(msgSettings["minWindowSize"]);
                if (msgSettings.ContainsKey("maxWindowSize"))
                    messageWindow.MaxWindowSize = Convert.ToSingle(msgSettings["maxWindowSize"]);
                if (msgSettings.ContainsKey("fontSize"))
                    messageWindow.FontSize = Convert.ToSingle(msgSettings["fontSize"]);
                if (msgSettings.ContainsKey("horizontalOffset"))
                    messageWindow.HorizontalOffset = Convert.ToSingle(msgSettings["horizontalOffset"]);
                if (msgSettings.ContainsKey("verticalOffset"))
                    messageWindow.VerticalOffset = Convert.ToSingle(msgSettings["verticalOffset"]);
            }
            display.AmbientOcclusionEnabled = (bool)snapshot["IsEnableAmbientOcclusion"];
            display.MsaaLevel = (int)snapshot["MsaaLevel"];
            display.AvatarShadowMode = (int)snapshot["AvatarShadow"];
            display.AvatarShadowResolution = (int)snapshot["AvatarShadowResolution"];
            display.BackgroundShadowMode = (int)snapshot["BackgroundShadow"];
            display.BackgroundShadowResolution = (int)snapshot["BackgroundShadowResolution"];
            display.AvatarWindowSize = (double)snapshot["WindowSize"] > 0 ? (int)(double)snapshot["WindowSize"] : 650;
        }

        private void CaptureKeyButton_Click(object sender, RoutedEventArgs e)
        {
            string originalText = VirtualKeyStringTextBox.Text;
            VirtualKeyStringTextBox.Text = "Press the key";
            VirtualKeyStringTextBox.Focus();

            _isCapturingKey = true;
            _isWinKeyPressed = false;
            _isAltKeyPressed = false;

            this.PreviewKeyDown += CaptureKey_PreviewKeyDown;
        }

        private void CaptureKey_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            e.Handled = true;
            string keyName = e.Key.ToString();

            bool isAltPressed = _isAltKeyPressed;
            bool isCtrlPressed = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            bool isShiftPressed = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
            bool isWinPressed = _isWinKeyPressed;

            if (keyName == "LeftAlt" || keyName == "RightAlt" ||
                keyName == "LeftCtrl" || keyName == "RightCtrl" ||
                keyName == "LeftShift" || keyName == "RightShift" ||
                keyName == "LWin" || keyName == "RWin")
            {
                return;
            }

            string result = "";
            if (isCtrlPressed) result += "Ctrl+";
            if (isAltPressed) result += "Alt+";
            if (isShiftPressed) result += "Shift+";
            if (isWinPressed) result += "Win+";
            result += keyName;

            VirtualKeyStringTextBox.Text = result;

            _isCapturingKey = false;
            _isWinKeyPressed = false;
            _isAltKeyPressed = false;
            this.PreviewKeyDown -= CaptureKey_PreviewKeyDown;
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (_isCapturingKey)
            {
                switch (msg)
                {
                    case WM_KEYDOWN:
                    case WM_SYSKEYDOWN:
                        int vkCode = wParam.ToInt32();
                        if (vkCode == VK_LWIN || vkCode == VK_RWIN ||
                            vkCode == VK_LALT || vkCode == VK_RALT)
                        {
                            handled = true;
                        }
                        break;
                }
            }
            return IntPtr.Zero;
        }

        private IntPtr SetHook(LowLevelKeyboardProc proc)
        {
            using (var curProcess = Process.GetCurrentProcess())
            using (var curModule = curProcess.MainModule)
            {
                return SetWindowsHookEx(WH_KEYBOARD_LL, proc,
                    GetModuleHandle(curModule?.ModuleName ?? string.Empty), 0);
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && _isCapturingKey)
            {
                int vkCode = Marshal.ReadInt32(lParam);
                if (vkCode == VK_LWIN || vkCode == VK_RWIN)
                {
                    if ((int)wParam == WM_KEYDOWN || (int)wParam == WM_SYSKEYDOWN)
                    {
                        _isWinKeyPressed = true;
                        return (IntPtr)1;
                    }
                    else if ((int)wParam == WM_KEYUP || (int)wParam == WM_SYSKEYUP)
                    {
                        _isWinKeyPressed = false;
                    }
                }
                else if (vkCode == VK_LALT || vkCode == VK_RALT)
                {
                    if ((int)wParam == WM_KEYDOWN || (int)wParam == WM_SYSKEYDOWN)
                    {
                        _isAltKeyPressed = true;
                        return (IntPtr)1;
                    }
                    else if ((int)wParam == WM_KEYUP || (int)wParam == WM_SYSKEYUP)
                    {
                        _isAltKeyPressed = false;
                    }
                }
            }
            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }

        private void ResetAvatarPositionButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var display = AppSettings.Instance.ConsoleClient.Display;
                display.AvatarPositionX = float.MinValue;
                display.AvatarPositionY = float.MinValue;
                display.RestoreWindowPosition = false;
                RestoreWindowPositionCheckBox.IsChecked = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"アバター位置のリセット中にエラーが発生しました: {ex.Message}",
                    "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
