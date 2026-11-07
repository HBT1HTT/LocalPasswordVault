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
            }
            catch (StorageException ex)
            {
                ShowError(ex.Message);
            }
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
