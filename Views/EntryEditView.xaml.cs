using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using LocalPasswordVault.Models;
using LocalPasswordVault.Services;

namespace LocalPasswordVault.Views
{
    public partial class EntryEditView : UserControl
    {
        private readonly EntryStore _store;
        private readonly EntryDetail? _existing;

        public event EventHandler<long>? Saved;
        public event EventHandler? Cancelled;

        public EntryEditView(EntryStore store, EntryDetail? existing)
        {
            _store = store;
            _existing = existing;
            InitializeComponent();

            TitleText.Text = existing is null ? "新建密码条目" : "编辑密码条目";
            if (existing is not null)
            {
                NameBox.Text = existing.Name;
                UsernameBox.Text = existing.Username;
                PasswordBox.Text = existing.Password;
                ExtraBox.Text = existing.Extra;
            }
        }

        public void FocusName()
        {
            Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    NameBox.Focus();
                    NameBox.SelectAll();
                }),
                DispatcherPriority.Input);
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            var name = NameBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                ShowError("名称不能为空，请填写名称。");
                NameBox.Focus();
                return;
            }

            try
            {
                long id;
                if (_existing is null)
                {
                    id = _store.Create(name, UsernameBox.Text, PasswordBox.Text, ExtraBox.Text);
                    Log.Info("新建条目成功");
                }
                else
                {
                    _store.Update(_existing.Id, name, UsernameBox.Text, PasswordBox.Text, ExtraBox.Text);
                    id = _existing.Id;
                    Log.Info("更新条目成功");
                }

                ClearInputs();
                Saved?.Invoke(this, id);
            }
            catch (StorageException ex)
            {
                ShowError(ex.Message);
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            ClearInputs();
            Cancelled?.Invoke(this, EventArgs.Empty);
        }

        private void ClearInputs()
        {
            NameBox.Text = string.Empty;
            UsernameBox.Text = string.Empty;
            PasswordBox.Text = string.Empty;
            ExtraBox.Text = string.Empty;
        }

        private void ShowError(string message)
        {
            ErrorText.Text = message;
            ErrorText.Visibility = Visibility.Visible;
        }
    }
}
