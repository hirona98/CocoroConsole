using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace CocoroConsole.Windows
{
    /// <summary>
    /// リマインダー編集ダイアログの表示モード。
    /// </summary>
    public enum ReminderDialogMode
    {
        /// <summary>
        /// 追加モード（「追加」ボタンを表示し、モーダルにしない運用を想定）。
        /// </summary>
        Add = 0,

        /// <summary>
        /// 編集モード（「OK/キャンセル」ボタンを表示）。
        /// </summary>
        Edit = 1
    }

    /// <summary>
    /// ReminderEditDialog の入力結果。
    /// UI から API モデルへ変換するための中間表現として利用する。
    /// </summary>
    public sealed class ReminderEditResult
    {
        /// <summary>
        /// 有効/無効。
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// 通知内容（必須）。
        /// </summary>
        public string Content { get; set; } = string.Empty;

        /// <summary>
        /// 繰り返し種別（once|daily|weekly）。
        /// </summary>
        public string RepeatKind { get; set; } = string.Empty; // once|daily|weekly

        /// <summary>
        /// 時（0-23）。
        /// </summary>
        public int Hour { get; set; } // 0-23

        /// <summary>
        /// 分（0-59）。
        /// </summary>
        public int Minute { get; set; } // 0-59

        /// <summary>
        /// 単発(once) の場合の日付。
        /// </summary>
        public DateTime? OnceDate { get; set; } // repeat_kind=once のときのみ

        /// <summary>
        /// 毎週(weekly) の場合の曜日（sun..sat）。
        /// </summary>
        public List<string>? Weekdays { get; set; } // sun..sat
    }

    /// <summary>
    /// リマインダーの追加/編集を行うウィンドウ。
    /// 
    /// - mode=Edit: OK で確定して閉じる
    /// - mode=Add: 「追加」押下でコールバックを呼び、入力をクリアして続けて追加できる
    /// </summary>
    public partial class ReminderEditDialog : Window
    {
        private readonly ReminderDialogMode _mode;
        private readonly Action<ReminderEditResult>? _onAdd;
        private readonly Action<ReminderEditResult>? _onOk;

        /// <summary>
        /// OK 押下で確定した結果（Edit モード想定）。
        /// </summary>
        public ReminderEditResult? Result { get; private set; }

        public ReminderEditDialog(
            ReminderEditResult? initial = null,
            ReminderDialogMode mode = ReminderDialogMode.Edit,
            Action<ReminderEditResult>? onAdd = null,
            Action<ReminderEditResult>? onOk = null)
        {
            InitializeComponent();

            _mode = mode;
            _onAdd = onAdd;
            _onOk = onOk;

            ApplyMode();
            InitializeTimePickers();

            if (initial != null)
            {
                // 既存値の復元（編集時）
                EnabledCheckBox.IsChecked = initial.Enabled;
                ContentTextBox.Text = initial.Content ?? string.Empty;
                SetTime(initial.Hour, initial.Minute);
                OnceDatePicker.SelectedDate = initial.OnceDate;

                SetRepeatKind(initial.RepeatKind);
                SetWeekdays(initial.Weekdays);
            }
            else
            {
                // 追加時の既定値
                SetTime(9, 0);
                OnceDatePicker.SelectedDate = DateTime.Today;
            }

            UpdateInputsEnabledState();
        }

        private void ApplyMode()
        {
            if (EditButtonsPanel == null || AddButtonsPanel == null)
            {
                return;
            }

            var isAdd = _mode == ReminderDialogMode.Add;
            EditButtonsPanel.Visibility = isAdd ? Visibility.Collapsed : Visibility.Visible;
            AddButtonsPanel.Visibility = isAdd ? Visibility.Visible : Visibility.Collapsed;

            if (isAdd)
            {
                // 追加ダイアログは「表示位置を呼び出し側が決める」運用があるため Manual に寄せる
                WindowStartupLocation = WindowStartupLocation.Manual;
                ResizeMode = ResizeMode.NoResize;
            }
        }

        private void InitializeTimePickers()
        {
            // 00-23 / 00-59 の固定リスト
            if (HourComboBox != null)
            {
                HourComboBox.Items.Clear();
                for (int h = 0; h <= 23; h++)
                {
                    HourComboBox.Items.Add(h.ToString("00"));
                }
            }

            if (MinuteComboBox != null)
            {
                MinuteComboBox.Items.Clear();
                for (int m = 0; m <= 59; m++)
                {
                    MinuteComboBox.Items.Add(m.ToString("00"));
                }
            }
        }

        private void SetTime(int hour, int minute)
        {
            if (HourComboBox != null)
            {
                HourComboBox.SelectedItem = Math.Clamp(hour, 0, 23).ToString("00");
            }

            if (MinuteComboBox != null)
            {
                MinuteComboBox.SelectedItem = Math.Clamp(minute, 0, 59).ToString("00");
            }
        }

        private void SetRepeatKind(string? repeatKind)
        {
            var normalized = (repeatKind ?? string.Empty).Trim().ToLowerInvariant();
            foreach (var item in RepeatKindComboBox.Items.OfType<ComboBoxItem>())
            {
                if (string.Equals(item.Tag?.ToString(), normalized, StringComparison.OrdinalIgnoreCase))
                {
                    RepeatKindComboBox.SelectedItem = item;
                    return;
                }
            }

            RepeatKindComboBox.SelectedIndex = 0;
        }

        private void SetWeekdays(List<string>? weekdays)
        {
            var set = new HashSet<string>((weekdays ?? new List<string>()).Select(x => x.Trim().ToLowerInvariant()));
            foreach (var cb in GetWeekdayCheckBoxes())
            {
                cb.IsChecked = cb.Tag is string tag && set.Contains(tag);
            }
        }

        private IEnumerable<CheckBox> GetWeekdayCheckBoxes()
        {
            yield return SunCheckBox;
            yield return MonCheckBox;
            yield return TueCheckBox;
            yield return WedCheckBox;
            yield return ThuCheckBox;
            yield return FriCheckBox;
            yield return SatCheckBox;
        }

        private void RepeatKindComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateInputsEnabledState();
        }

        private void UpdateInputsEnabledState()
        {
            if (WeekdaysBorder == null || OnceDateBorder == null || RepeatKindComboBox == null)
            {
                return;
            }

            var repeatKind = (RepeatKindComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            var isWeekly = string.Equals(repeatKind, "weekly", StringComparison.OrdinalIgnoreCase);
            var isOnce = string.Equals(repeatKind, "once", StringComparison.OrdinalIgnoreCase);

            WeekdaysBorder.IsEnabled = isWeekly;
            OnceDateBorder.IsEnabled = isOnce;
        }

        private bool TryGetSelectedTime(out int hour, out int minute)
        {
            hour = 0;
            minute = 0;

            var hourText = HourComboBox?.SelectedItem?.ToString();
            var minuteText = MinuteComboBox?.SelectedItem?.ToString();
            if (string.IsNullOrWhiteSpace(hourText) || string.IsNullOrWhiteSpace(minuteText))
            {
                return false;
            }

            if (!int.TryParse(hourText, out hour) || !int.TryParse(minuteText, out minute))
            {
                return false;
            }

            return hour >= 0 && hour <= 23 && minute >= 0 && minute <= 59;
        }

        private bool TryBuildResult(out ReminderEditResult result)
        {
            result = new ReminderEditResult();

            var enabled = EnabledCheckBox.IsChecked ?? false;
            var content = (ContentTextBox.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(content))
            {
                // 内容は必須（サーバーに空文字を送らない）
                MessageBox.Show("内容を入力してください。", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            var repeatKind = (RepeatKindComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "daily";
            repeatKind = repeatKind.Trim().ToLowerInvariant();
            if (!string.Equals(repeatKind, "once", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(repeatKind, "daily", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(repeatKind, "weekly", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("繰り返し種別が不正です。", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (!TryGetSelectedTime(out var hour, out var minute))
            {
                MessageBox.Show("時刻を選択してください。", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            List<string>? weekdays = null;
            if (string.Equals(repeatKind, "weekly", StringComparison.OrdinalIgnoreCase))
            {
                weekdays = GetWeekdayCheckBoxes()
                    .Where(cb => cb.IsChecked == true)
                    .Select(cb => cb.Tag?.ToString())
                    .Where(tag => !string.IsNullOrWhiteSpace(tag))
                    .Select(tag => tag!.Trim().ToLowerInvariant())
                    .ToList();

                if (weekdays.Count == 0)
                {
                    MessageBox.Show("毎週の場合は曜日を1つ以上選択してください。", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }
            }

            DateTime? onceDate = null;
            if (string.Equals(repeatKind, "once", StringComparison.OrdinalIgnoreCase))
            {
                onceDate = OnceDatePicker?.SelectedDate;
                if (onceDate == null)
                {
                    MessageBox.Show("単発の場合は日付を選択してください。", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }
            }

            result = new ReminderEditResult
            {
                Enabled = enabled,
                Content = content,
                RepeatKind = repeatKind,
                Hour = hour,
                Minute = minute,
                OnceDate = onceDate,
                Weekdays = weekdays
            };

            return true;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (!TryBuildResult(out var result))
            {
                return;
            }

            Result = result;
            _onOk?.Invoke(result);
            Close();
        }

        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            if (!TryBuildResult(out var result))
            {
                return;
            }

            _onAdd?.Invoke(result);

            ContentTextBox.Text = string.Empty;
            ContentTextBox.Focus();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
