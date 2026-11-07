using System.Windows;
using System.Windows.Controls;
using LocalPasswordVault.Models;
using LocalPasswordVault.Services;

namespace LocalPasswordVault.Views
{
    public partial class MainView : UserControl
    {
        private readonly EntryStore _store;
        private EntryDetail? _detail;
        private bool _passwordVisible;

        public event EventHandler? NewRequested;
        public event EventHandler<EntryDetail>? EditRequested;

        public MainView(EntryStore store)
        {
            _store = store;
            InitializeComponent();
            Unloaded += (_, _) => ClearDetail();
        }

        /// <summary>刷新条目名称列表，可指定刷新后选中的条目。</summary>
        public void RefreshList(long? selectId)
        {
            try
            {
                var items = _store.List();
                EntryList.ItemsSource = items;
                EmptyState.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

                ClearDetail();
                ShowDetailEmpty();

                if (selectId.HasValue)
                {
                    var target = items.FirstOrDefault(item => item.Id == selectId.Value);
                    if (target is not null)
                    {
                        EntryList.SelectedItem = target;
                    }
                }

                UpdateSelectionState();
            }
            catch (StorageException ex)
            {
                ShowError(ex.Message);
            }
        }

        private void UpdateSelectionState()
        {
            var hasSelection = EntryList.SelectedItem is EntrySummary;
            DeleteButton.IsEnabled = hasSelection;
            SelectionHint.Visibility = EntryList.Items.Count > 0 && !hasSelection
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var mainWindow = Window.GetWindow(this) as MainWindow;
            // 设置窗口打开期间暂停闲置锁定计时，避免用户在设置里操作时被判定为闲置
            mainWindow?.PauseIdleLock();
            try
            {
                var window = new SettingsWindow(_store) { Owner = Window.GetWindow(this) };
                window.ShowDialog();

                // 导入会替换数据库文件，回到锁定状态让界面重新从新库加载
                if (window.ImportCompleted)
                {
                    mainWindow?.LockAfterDataImport();
                }
            }
            finally
            {
                mainWindow?.ResumeIdleLock();
            }
        }

        private async void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = EntryList.SelectedItem as EntrySummary;
            if (selected is null)
            {
                MessageBox.Show(Window.GetWindow(this), "请先选择一个条目", "删除", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var owner = Window.GetWindow(this);
            DeleteButton.IsEnabled = false;

            try
            {
                // 无可用验证手段时一律阻止删除，避免绕过系统验证
                if (!await HelloAuthService.IsAvailableAsync())
                {
                    Log.Info("删除被阻止：Windows Hello 不可用");
                    MessageBox.Show(
                        owner,
                        "当前 Windows 未配置 PIN 或 Windows Hello，请先在系统设置中配置后再使用。",
                        "删除未完成",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                if (_store.GetDeleteRequiresVerification())
                {
                    // 验证前先盖上应用内遮挡层，验证窗口被拖动时后方不会露出条目内容
                    var mainWindow = Window.GetWindow(this) as MainWindow;
                    var verified = mainWindow is not null
                        ? await mainWindow.VerifyWithShieldAsync("请验证身份以删除该条目")
                        : await HelloAuthService.VerifyAsync("请验证身份以删除该条目");
                    if (!verified)
                    {
                        Log.Info("删除前身份验证未通过");
                        MessageBox.Show(owner, "验证未通过，条目未删除。", "删除未完成", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }

                if (EntryList.SelectedItem is not EntrySummary current || current.Id != selected.Id)
                {
                    Log.Info("删除已取消：选中条目已变更");
                    MessageBox.Show(owner, "选择已变更，删除已取消。", "删除已取消", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var confirm = MessageBox.Show(
                    owner,
                    $"确定要删除“{selected.Name}”吗？\n\n删除后无法恢复。",
                    "删除确认",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No);
                if (confirm != MessageBoxResult.Yes)
                {
                    Log.Info("删除取消");
                    return;
                }

                try
                {
                    _store.Delete(selected.Id);
                    Log.Info("删除条目成功");

                    RefreshList(null);
                    ShowDetailEmpty();
                    UpdateSelectionState();
                    MessageBox.Show(owner, "条目已删除。", "已删除", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (StorageException ex)
                {
                    Log.Error("删除条目失败", ex);
                    MessageBox.Show(owner, ex.Message, "删除失败", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                // 兜底：验证流程异常时条目未被删除，界面状态保持不变
                Log.Error("删除流程异常", ex);
                MessageBox.Show(owner, "操作未能完成，条目未删除。", "删除未完成", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                UpdateSelectionState();
            }
        }

        /// <summary>闲置自动锁定时调用：清空详情区已解密内容与列表数据，避免界面残留敏感信息。</summary>
        public void ClearSensitiveState()
        {
            EntryList.SelectedItem = null;
            EntryList.ItemsSource = null;
            ClearDetail();
            ShowDetailEmpty();
            UpdateSelectionState();
        }

        private void NewButton_Click(object sender, RoutedEventArgs e) => NewRequested?.Invoke(this, EventArgs.Empty);

        private void EditButton_Click(object sender, RoutedEventArgs e)
        {
            if (_detail is not null)
            {
                EditRequested?.Invoke(this, _detail);
            }
        }

        private void EntryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ClearDetail();
            var selected = EntryList.SelectedItem as EntrySummary;
            UpdateSelectionState();
            if (selected is null)
            {
                ShowDetailEmpty();
                return;
            }

            LoadDetail(selected.Id);
        }

        private void LoadDetail(long id)
        {
            try
            {
                var detail = _store.Get(id);
                _detail = detail;
                _passwordVisible = false;

                DetailName.Text = detail.Name;
                UsernameText.Text = detail.Username;
                ExtraText.Text = detail.Extra;
                TogglePasswordButton.Content = "显示";
                RenderPassword();

                DetailPanel.Visibility = Visibility.Visible;
                DetailEmptyPanel.Visibility = Visibility.Collapsed;
                DetailErrorPanel.Visibility = Visibility.Collapsed;

                Log.Info("查看条目详情");
            }
            catch (EntryDecryptionException)
            {
                Log.Info("条目解密失败，已提示用户");
                ShowError("无法解密该条目，可能数据库来自其他 Windows 用户或其他电脑");
            }
            catch (StorageException ex)
            {
                ShowError(ex.Message);
            }
        }

        private void TogglePasswordButton_Click(object sender, RoutedEventArgs e)
        {
            if (_detail is null)
            {
                return;
            }

            _passwordVisible = !_passwordVisible;
            TogglePasswordButton.Content = _passwordVisible ? "隐藏" : "显示";
            RenderPassword();
        }

        private void RenderPassword()
        {
            if (_detail is null || string.IsNullOrEmpty(_detail.Password))
            {
                PasswordText.Text = string.Empty;
                return;
            }

            PasswordText.Text = _passwordVisible ? _detail.Password : new string('●', 8);
        }

        /// <summary>清空详情区所有字段，界面不残留密码明文。</summary>
        private void ClearDetail()
        {
            _detail = null;
            _passwordVisible = false;

            DetailName.Text = string.Empty;
            UsernameText.Text = string.Empty;
            PasswordText.Text = string.Empty;
            ExtraText.Text = string.Empty;
            TogglePasswordButton.Content = "显示";

            DetailPanel.Visibility = Visibility.Collapsed;
        }

        private void ShowDetailEmpty()
        {
            DetailEmptyPanel.Visibility = Visibility.Visible;
            DetailErrorPanel.Visibility = Visibility.Collapsed;
            DetailPanel.Visibility = Visibility.Collapsed;
        }

        private void ShowError(string message)
        {
            DetailErrorText.Text = message;
            DetailErrorPanel.Visibility = Visibility.Visible;
            DetailEmptyPanel.Visibility = Visibility.Collapsed;
            DetailPanel.Visibility = Visibility.Collapsed;
        }
    }
}
