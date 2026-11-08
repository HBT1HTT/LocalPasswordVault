using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using LocalPasswordVault.Models;
using LocalPasswordVault.Services;
using LocalPasswordVault.Views;

namespace LocalPasswordVault
{
    public partial class MainWindow : Window
    {
        private EntryStore? _store;
        private bool _unlocked;
        private bool _idlePaused;
        private int _verificationActive;
        private DateTime _lastActivity = DateTime.UtcNow;
        private DispatcherTimer? _idleTimer;
        private int _autoLockMinutes = EntryStore.DefaultAutoLockMinutes;

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

        private void ShowUnlock(bool autoVerify = true)
        {
            var view = new UnlockView(autoVerify);
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
            _unlocked = true;
            _autoLockMinutes = _store.GetAutoLockMinutes();

            var view = new MainView(_store);
            view.NewRequested += (_, _) => ShowEdit(null);
            view.EditRequested += (_, detail) => ShowEdit(detail);
            RootContent.Content = view;
            view.RefreshList(selectId);

            TouchActivity();
            StartIdleTimer();
        }

        private void ShowEdit(EntryDetail? detail)
        {
            if (_store is null)
            {
                return;
            }

            TouchActivity();

            var view = new EntryEditView(_store, detail);
            view.Saved += (_, id) => ShowMain(id);
            view.Cancelled += (_, _) => ShowMain(detail?.Id);
            RootContent.Content = view;
            view.FocusName();
        }

        /// <summary>
        /// 在窗口内盖上不透明遮挡后再唤起 Windows Hello，结束后撤掉遮挡。
        /// 遮挡是应用自己窗口的一部分，系统验证窗口被拖动时其后方只会看到封面。
        /// </summary>
        public async Task<bool> VerifyWithShieldAsync(string message)
        {
            _verificationActive++;
            VerificationShield.Visibility = Visibility.Visible;
            try
            {
                // 先让遮挡完成一次布局与渲染，再弹出系统验证窗口，避免验证窗口后方短暂可见
                UpdateLayout();
                await Task.Delay(60);
                return await HelloAuthService.VerifyAsync(message);
            }
            finally
            {
                VerificationShield.Visibility = Visibility.Collapsed;
                _verificationActive--;
                TouchActivity();
            }
        }

        /// <summary>被第二个实例唤起时把已运行的窗口切到前台（不解锁、不显示数据）。</summary>
        public void ActivateFromExternalInstance()
        {
            if (Dispatcher.CheckAccess() is false)
            {
                Dispatcher.BeginInvoke(new Action(ActivateFromExternalInstance));
                return;
            }

            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }

            Show();
            Activate();
        }

        public void PauseIdleLock() => _idlePaused = true;

        public void ResumeIdleLock()
        {
            _autoLockMinutes = _store?.GetAutoLockMinutes() ?? EntryStore.DefaultAutoLockMinutes;
            _idlePaused = false;
            TouchActivity();
        }

        private void StartIdleTimer()
        {
            if (_idleTimer is null)
            {
                _idleTimer = new DispatcherTimer(DispatcherPriority.Background);
                _idleTimer.Interval = TimeSpan.FromSeconds(1);
                _idleTimer.Tick += IdleTimer_Tick;
            }

            _idleTimer.Start();
        }

        private void IdleTimer_Tick(object? sender, EventArgs e)
        {
            if (_unlocked is false || _idlePaused || _verificationActive > 0 || _autoLockMinutes <= 0)
            {
                return;
            }

            if ((DateTime.UtcNow - _lastActivity).TotalMinutes >= _autoLockMinutes)
            {
                LockDueToIdle();
            }
        }

        private void LockDueToIdle()
        {
            if (_unlocked is false)
            {
                return;
            }

            Log.Info("闲置超时，自动锁定");
            LockNow(autoVerify: false);
        }

        /// <summary>导入替换数据库后调用：清空内存中的旧数据并回到锁定页。</summary>
        public void LockAfterDataImport()
        {
            if (_unlocked is false)
            {
                return;
            }

            Log.Info("导入完成，回到锁定状态");
            LockNow(autoVerify: true);
        }

        private void LockNow(bool autoVerify)
        {
            _unlocked = false;
            _idlePaused = false;
            _idleTimer?.Stop();

            // 先清空所有已解密内容与列表，再把界面换成锁定页
            ClearSensitiveContent();
            RootContent.Content = null;
            ShowUnlock(autoVerify);

            if (!autoVerify)
            {
                Log.Info("闲置锁定：已清空敏感内容，解锁页等待用户点击验证身份（不自动调用 Windows Hello）");
            }
        }

        private void ClearSensitiveContent()
        {
            switch (RootContent.Content)
            {
                case MainView main:
                    main.ClearSensitiveState();
                    break;
                case EntryEditView edit:
                    edit.ClearSensitiveState();
                    break;
            }
        }

        private void TouchActivity()
        {
            _lastActivity = DateTime.UtcNow;
        }

        private void UserActivity_MouseMove(object sender, MouseEventArgs e) => OnUserActivity();

        private void UserActivity_MouseDown(object sender, MouseButtonEventArgs e) => OnUserActivity();

        private void UserActivity_MouseWheel(object sender, MouseWheelEventArgs e) => OnUserActivity();

        private void UserActivity_KeyDown(object sender, KeyEventArgs e) => OnUserActivity();

        private void OnUserActivity()
        {
            if (_unlocked)
            {
                TouchActivity();
            }
        }
    }
}
