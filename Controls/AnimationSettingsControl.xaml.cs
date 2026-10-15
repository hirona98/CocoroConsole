using CocoroConsole.Models.OtomeKairoApi;
using CocoroConsole.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace CocoroConsole.Controls
{
    /// <summary>
    /// AnimationSettingsControl.xaml の相互作用ロジック
    /// </summary>
    public partial class AnimationSettingsControl : UserControl
    {
        /// <summary>
        /// 設定が変更されたときに発生するイベント
        /// </summary>
        public event EventHandler? SettingsChanged;

        /// <summary>
        /// アニメーション設定を保存するためのリスト
        /// </summary>
        private List<OtomeKairoConsoleAnimationSet> _animationSettings = new List<OtomeKairoConsoleAnimationSet>();

        /// <summary>
        /// 読み込み完了フラグ
        /// </summary>
        private bool _isInitialized = false;

        /// <summary>
        /// 通信サービス
        /// </summary>
        private ICommunicationService? _communicationService;

        /// <summary>
        /// 名前変更のデバウンス用タイマー
        /// </summary>
        private DispatcherTimer? _nameChangeTimer;

        /// <summary>
        /// デバウンス遅延時間（ミリ秒）
        /// </summary>
        private const int DEBOUNCE_DELAY_MS = 200; // 300ms → 200ms に短縮

        public AnimationSettingsControl()
        {
            InitializeComponent();

            // 名前変更用のデバウンスタイマーを初期化
            _nameChangeTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(DEBOUNCE_DELAY_MS)
            };
            _nameChangeTimer.Tick += NameChangeTimer_Tick;
        }

        /// <summary>
        /// 通信サービスを設定
        /// </summary>
        public void SetCommunicationService(ICommunicationService communicationService)
        {
            _communicationService = communicationService;
        }

        /// <summary>
        /// 初期化処理
        /// </summary>
        public void Initialize()
        {
            try
            {
                var motion = AppSettings.Instance.ConsoleClient.Motion;
                _animationSettings = motion.AnimationSets
                    .Select(AppSettings.CloneDocument)
                    .ToList();

                UpdateAnimationUI();

                _isInitialized = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"アニメーション設定の初期化エラー: {ex.Message}", "エラー",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// アニメーションUIを更新
        /// </summary>
        private void UpdateAnimationUI()
        {
            // ComboBoxの更新
            AnimationSetComboBox.ItemsSource = _animationSettings;

            if (_animationSettings.Count > 0)
            {
                var selectedId = AppSettings.Instance.ConsoleClient.Motion.SelectedAnimationSetId;
                var animationIndex = _animationSettings.FindIndex(animationSet =>
                    animationSet.AnimationSetId == selectedId);

                if (animationIndex >= 0 && animationIndex < _animationSettings.Count)
                {
                    AnimationSetComboBox.SelectedIndex = animationIndex;
                    var animSetting = _animationSettings[animationIndex];
                    if (animSetting != null)
                    {
                        UpdateAnimationListPanel(animSetting.Animations);
                    }
                }
                else
                {
                    // インデックスが無効な場合は最初の項目を選択
                    AnimationSetComboBox.SelectedIndex = 0;
                    UpdateAnimationListPanel(_animationSettings[0].Animations);
                }
            }

            // イベントハンドラーの設定
            AnimationSetComboBox.SelectionChanged -= AnimationSetComboBox_SelectionChanged;
            AnimationSetComboBox.SelectionChanged += AnimationSetComboBox_SelectionChanged;

            // 姿勢変更ループ回数と名前の設定
            if (_animationSettings.Count > 0 && AnimationSetComboBox.SelectedIndex >= 0)
            {
                var currentSetting = _animationSettings[AnimationSetComboBox.SelectedIndex];
                AnimationSetNameTextBox.Text = currentSetting.DisplayName;
                PostureChangeLoopCountStandingTextBox.Text = currentSetting.PostureChangeLoopCountStanding.ToString();
                PostureChangeLoopCountSittingFloorTextBox.Text = currentSetting.PostureChangeLoopCountSittingFloor.ToString();
            }
        }

        /// <summary>
        /// アニメーションリストパネルを更新
        /// </summary>
        private void UpdateAnimationListPanel(List<OtomeKairoConsoleAnimation> Animations)
        {
            AnimationListPanel.Children.Clear();

            // AnimationTypeでグループ化
            var groupedAnimations = Animations.GroupBy(a => a.AnimationType).OrderBy(g => g.Key);

            foreach (var group in groupedAnimations)
            {
                // 折りたたみ可能なExpander（枠線付き）を作成
                var expander = new Expander
                {
                    Header = GetAnimationTypeDisplayName(group.Key),
                    Margin = new Thickness(0, 0, 0, 10),
                    IsExpanded = group.Key != 0 && group.Key != 1
                };

                expander.Style = (Style)FindResource("GroupBoxLikeExpanderStyle");

                // グループボックス内のスタックパネル
                var stackPanel = new StackPanel();

                foreach (var animation in group)
                {
                    var grid = new Grid();
                    grid.Margin = new Thickness(0, 5, 0, 5);
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                    // Playボタン
                    var playButton = new Button
                    {
                        Content = "Play",
                        Margin = new Thickness(0, 0, 10, 0),
                        Padding = new Thickness(10, 5, 10, 5),
                        Tag = animation,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    playButton.Click += PlayAnimationButton_Click;
                    Grid.SetColumn(playButton, 0);

                    // チェックボックス
                    var checkBox = new CheckBox
                    {
                        Content = animation.DisplayName,
                        IsChecked = animation.Enabled,
                        Tag = animation,
                        Margin = new Thickness(0, 0, 0, 0),
                        VerticalAlignment = VerticalAlignment.Center,
                        Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0xFF, 0x48, 0x73, 0xCF))
                    };
                    checkBox.Checked += AnimationCheckBox_Checked;
                    checkBox.Unchecked += AnimationCheckBox_Unchecked;
                    Grid.SetColumn(checkBox, 1);

                    grid.Children.Add(playButton);
                    grid.Children.Add(checkBox);

                    stackPanel.Children.Add(grid);
                }

                expander.Content = stackPanel;
                AnimationListPanel.Children.Add(expander);
            }
        }

        /// <summary>
        /// アニメーションタイプの表示名を取得
        /// </summary>
        private string GetAnimationTypeDisplayName(int AnimationType)
        {
            switch (AnimationType)
            {
                case 0:
                    return "立ち姿勢モーション ON/OFF";
                case 1:
                    return "座り姿勢モーション ON/OFF";
                case 2:
                    return "寝そべりモーション ON/OFF";
                default:
                    return "Unknown";
            }
        }

        /// <summary>
        /// アニメーションチェックボックスのチェック時の処理
        /// </summary>
        private void AnimationCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkBox && checkBox.Tag is OtomeKairoConsoleAnimation animation)
            {
                animation.Enabled = true;
                SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// アニメーションチェックボックスのアンチェック時の処理
        /// </summary>
        private void AnimationCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkBox && checkBox.Tag is OtomeKairoConsoleAnimation animation)
            {
                animation.Enabled = false;
                SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// アニメーション再生ボタンクリック時の処理
        /// </summary>
        private async void PlayAnimationButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is OtomeKairoConsoleAnimation animation)
            {
                if (_communicationService != null)
                {
                    try
                    {
                        // CocoroShellにアニメーション再生指示を送信
                        await _communicationService.SendAnimationToShellAsync(animation.AnimationName);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"アニメーション再生エラー: {ex.Message}");
                        // エラー表示は省略（UIHelperが存在しない可能性があるため）
                        MessageBox.Show($"アニメーション再生エラー: {ex.Message}", "エラー",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                else
                {
                    MessageBox.Show("通信サービスが利用できません。", "通信エラー",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        /// <summary>
        /// アニメーションセット選択変更イベント
        /// </summary>
        private void AnimationSetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized || AnimationSetComboBox.SelectedIndex < 0)
                return;

            var animSetting = _animationSettings[AnimationSetComboBox.SelectedIndex];
            if (animSetting != null)
            {
                // 名前とループ回数を更新
                AnimationSetNameTextBox.Text = animSetting.DisplayName;
                PostureChangeLoopCountStandingTextBox.Text = animSetting.PostureChangeLoopCountStanding.ToString();
                PostureChangeLoopCountSittingFloorTextBox.Text = animSetting.PostureChangeLoopCountSittingFloor.ToString();

                UpdateAnimationListPanel(animSetting.Animations);
            }
        }

        /// <summary>
        /// アニメーションセット名のテキスト変更イベント（リアルタイム更新）
        /// </summary>
        private void AnimationSetNameTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isInitialized || AnimationSetComboBox.SelectedIndex < 0)
                return;

            // タイマーがすでに動作中の場合はリセット
            if (_nameChangeTimer != null)
            {
                _nameChangeTimer.Stop();
                _nameChangeTimer.Start();
            }
        }

        /// <summary>
        /// 名前変更タイマーのTickイベント（デバウンス処理）
        /// </summary>
        private void NameChangeTimer_Tick(object? sender, EventArgs e)
        {
            if (_nameChangeTimer != null)
            {
                _nameChangeTimer.Stop();
            }

            if (!_isInitialized || AnimationSetComboBox.SelectedIndex < 0)
                return;

            var selectedIndex = AnimationSetComboBox.SelectedIndex;
            if (selectedIndex >= 0 && selectedIndex < _animationSettings.Count)
            {
                var newName = AnimationSetNameTextBox.Text;
                if (!string.IsNullOrWhiteSpace(newName))
                {
                    // 現在選択されているアイテムのインデックスを保存
                    var currentSelectedIndex = selectedIndex;

                    // アニメーション設定の名前を更新
                    _animationSettings[selectedIndex].DisplayName = newName;

                    // ComboBoxのItemsSourceを一時的に無効にしてSelectionChangedイベントを防ぐ
                    AnimationSetComboBox.SelectionChanged -= AnimationSetComboBox_SelectionChanged;

                    // ComboBoxのItemsSourceを更新
                    AnimationSetComboBox.ItemsSource = null;
                    AnimationSetComboBox.ItemsSource = _animationSettings;

                    // 選択状態を復元
                    AnimationSetComboBox.SelectedIndex = currentSelectedIndex;

                    // SelectionChangedイベントハンドラーを再設定
                    AnimationSetComboBox.SelectionChanged += AnimationSetComboBox_SelectionChanged;

                    SettingsChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        /// <summary>
        /// アニメーションセット名変更イベント
        /// </summary>
        private void AnimationSetNameTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized || AnimationSetComboBox.SelectedIndex < 0)
                return;

            var selectedIndex = AnimationSetComboBox.SelectedIndex;
            if (selectedIndex >= 0 && selectedIndex < _animationSettings.Count)
            {
                var newName = AnimationSetNameTextBox.Text;
                if (!string.IsNullOrWhiteSpace(newName))
                {
                    _animationSettings[selectedIndex].DisplayName = newName;

                    // ComboBoxのItemsSourceを更新
                    AnimationSetComboBox.ItemsSource = null;
                    AnimationSetComboBox.ItemsSource = _animationSettings;
                    AnimationSetComboBox.SelectedIndex = selectedIndex;

                    SettingsChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        /// <summary>
        /// アニメーションセット追加ボタンクリック
        /// </summary>
        private void AddAnimationSetButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 現在選択されているアニメーションセットをベースに新しいセットを作成
                OtomeKairoConsoleAnimationSet? sourceSet = null;
                if (AnimationSetComboBox.SelectedIndex >= 0 && AnimationSetComboBox.SelectedIndex < _animationSettings.Count)
                {
                    sourceSet = _animationSettings[AnimationSetComboBox.SelectedIndex];
                }
                else if (_animationSettings.Count > 0)
                {
                    sourceSet = _animationSettings[0];
                }

                var newAnimationSet = new OtomeKairoConsoleAnimationSet
                {
                    AnimationSetId = $"animation_set:{Guid.NewGuid():N}",
                    DisplayName = $"New Animation Set {_animationSettings.Count + 1}",
                    Animations = new List<OtomeKairoConsoleAnimation>()
                };

                // 既存のアニメーションセットからアニメーションをコピーして（全て有効にする）
                if (sourceSet != null)
                {
                    foreach (var animation in sourceSet.Animations)
                    {
                        newAnimationSet.Animations.Add(new OtomeKairoConsoleAnimation
                        {
                            DisplayName = animation.DisplayName,
                            AnimationType = animation.AnimationType,
                            AnimationName = animation.AnimationName,
                            Enabled = true  // デフォルトでは全て有効
                        });
                    }

                    // 姿勢変更ループ回数はデフォルト値30を設定
                    newAnimationSet.PostureChangeLoopCountStanding = 30;
                    newAnimationSet.PostureChangeLoopCountSittingFloor = 30;
                }

                _animationSettings.Add(newAnimationSet);

                // ComboBoxのItemsSourceを更新
                AnimationSetComboBox.ItemsSource = null;
                AnimationSetComboBox.ItemsSource = _animationSettings;
                AnimationSetComboBox.SelectedIndex = _animationSettings.Count - 1;

                UpdateAnimationListPanel(newAnimationSet.Animations);

                // 姿勢変更ループ回数のテキストボックスも更新
                PostureChangeLoopCountStandingTextBox.Text = newAnimationSet.PostureChangeLoopCountStanding.ToString();
                PostureChangeLoopCountSittingFloorTextBox.Text = newAnimationSet.PostureChangeLoopCountSittingFloor.ToString();

                SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"アニメーションセット追加エラー: {ex.Message}", "エラー",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// アニメーションセット複製ボタンクリック
        /// </summary>
        private void DuplicateAnimationSetButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (AnimationSetComboBox.SelectedIndex < 0 || AnimationSetComboBox.SelectedIndex >= _animationSettings.Count)
                    return;

                var sourceSet = _animationSettings[AnimationSetComboBox.SelectedIndex];

                // 複製するアニメーションセットの名前を生成
                var newName = sourceSet.DisplayName + "_copy";

                // 同名のアニメーションセットが既に存在する場合は番号を付ける
                int copyNumber = 1;
                while (_animationSettings.Any(s => s.DisplayName == newName))
                {
                    newName = $"{sourceSet.DisplayName}_copy{copyNumber}";
                    copyNumber++;
                }

                var newAnimationSet = new OtomeKairoConsoleAnimationSet
                {
                    AnimationSetId = $"animation_set:{Guid.NewGuid():N}",
                    DisplayName = newName,
                    Animations = new List<OtomeKairoConsoleAnimation>(),
                    PostureChangeLoopCountStanding = sourceSet.PostureChangeLoopCountStanding,
                    PostureChangeLoopCountSittingFloor = sourceSet.PostureChangeLoopCountSittingFloor
                };

                // アニメーションをコピー
                foreach (var animation in sourceSet.Animations)
                {
                    newAnimationSet.Animations.Add(new OtomeKairoConsoleAnimation
                    {
                        DisplayName = animation.DisplayName,
                        AnimationType = animation.AnimationType,
                        AnimationName = animation.AnimationName,
                        Enabled = animation.Enabled
                    });
                }

                _animationSettings.Add(newAnimationSet);

                // ComboBoxのItemsSourceを更新
                AnimationSetComboBox.ItemsSource = null;
                AnimationSetComboBox.ItemsSource = _animationSettings;
                AnimationSetComboBox.SelectedIndex = _animationSettings.Count - 1;

                UpdateAnimationListPanel(newAnimationSet.Animations);

                // 名前と姿勢変更ループ回数のテキストボックスも更新
                AnimationSetNameTextBox.Text = newAnimationSet.DisplayName;
                PostureChangeLoopCountStandingTextBox.Text = newAnimationSet.PostureChangeLoopCountStanding.ToString();
                PostureChangeLoopCountSittingFloorTextBox.Text = newAnimationSet.PostureChangeLoopCountSittingFloor.ToString();

                SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"アニメーションセット複製エラー: {ex.Message}", "エラー",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// アニメーションセット削除ボタンクリック
        /// </summary>
        private void DeleteAnimationSetButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (AnimationSetComboBox.SelectedIndex < 0 || _animationSettings.Count <= 1)
                {
                    return;
                }

                var selectedIndex = AnimationSetComboBox.SelectedIndex;

                _animationSettings.RemoveAt(selectedIndex);

                // ComboBoxのItemsSourceを更新
                AnimationSetComboBox.ItemsSource = null;
                AnimationSetComboBox.ItemsSource = _animationSettings;

                if (_animationSettings.Count > 0)
                {
                    var newIndex = Math.Min(selectedIndex, _animationSettings.Count - 1);
                    AnimationSetComboBox.SelectedIndex = newIndex;
                    UpdateAnimationListPanel(_animationSettings[newIndex].Animations);
                }
                else
                {
                    AnimationListPanel.Children.Clear();
                }

                SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"アニメーションセット削除エラー: {ex.Message}", "エラー",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 立ち姿勢ループ回数変更イベント
        /// </summary>
        private void PostureChangeLoopCountStandingTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isInitialized || AnimationSetComboBox.SelectedIndex < 0)
                return;

            if (int.TryParse(PostureChangeLoopCountStandingTextBox.Text, out int value))
            {
                _animationSettings[AnimationSetComboBox.SelectedIndex].PostureChangeLoopCountStanding = value;
                SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// 座り姿勢ループ回数変更イベント
        /// </summary>
        private void PostureChangeLoopCountSittingFloorTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isInitialized || AnimationSetComboBox.SelectedIndex < 0)
                return;

            if (int.TryParse(PostureChangeLoopCountSittingFloorTextBox.Text, out int value))
            {
                _animationSettings[AnimationSetComboBox.SelectedIndex].PostureChangeLoopCountSittingFloor = value;
                SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// 現在のアニメーション設定を取得
        /// </summary>
        public List<OtomeKairoConsoleAnimationSet> GetAnimationSettings()
        {
            return new List<OtomeKairoConsoleAnimationSet>(_animationSettings);
        }

        /// <summary>
        /// 現在選択中のアニメーションセットインデックスを取得
        /// </summary>
        public int GetCurrentAnimationSettingIndex()
        {
            return AnimationSetComboBox.SelectedIndex;
        }
    }
}
