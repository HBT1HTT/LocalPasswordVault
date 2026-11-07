using System.Windows;
using LocalPasswordVault.Models;
using LocalPasswordVault.Services;
using LocalPasswordVault.Views;

namespace LocalPasswordVault
{
    public partial class MainWindow : Window
    {
        private EntryStore? _store;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Loaded -= MainWindow_Loaded;

            try
            {
                _store = new EntryStore(EntryStore.DefaultDatabasePath);
                _store.Initialize();
            }
            catch (StorageException ex)
            {
                // 数据库损坏或无法打开：提示后退出，不删除、不重建原文件
                MessageBox.Show(this, ex.Message, "本地密码库", MessageBoxButton.OK, MessageBoxImage.Error);
                Application.Current.Shutdown();
                return;
            }

            ShowUnlock();
        }

        private void ShowUnlock()
        {
            var view = new UnlockView();
            view.Unlocked += (_, _) => ShowMain(null);
            RootContent.Content = view;
        }

        private void ShowMain(long? selectId)
        {
            if (_store is null)
            {
                return;
            }

            Log.Info("进入主界面");
            var view = new MainView(_store);
            view.NewRequested += (_, _) => ShowEdit(null);
            view.EditRequested += (_, detail) => ShowEdit(detail);
            RootContent.Content = view;
            view.RefreshList(selectId);
        }

        private void ShowEdit(EntryDetail? detail)
        {
            if (_store is null)
            {
                return;
            }

            var view = new EntryEditView(_store, detail);
            view.Saved += (_, id) => ShowMain(id);
            view.Cancelled += (_, _) => ShowMain(detail?.Id);
            RootContent.Content = view;
            view.FocusName();
        }
    }
}
