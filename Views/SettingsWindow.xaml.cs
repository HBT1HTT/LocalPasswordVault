using System.IO;
using System.Windows;
using System.Windows.Controls;
using LocalPasswordVault.Services;
using Microsoft.Win32;

namespace LocalPasswordVault.Views
{
    public partial class SettingsWindow : Window
    {
        /// <summary>可选闲置锁定时长（分钟），0 表示关闭自动锁定。</summary>
        private static readonly int[] AutoLockMinutesOptions = { 1, 5, 10, 30, 0 };

        private readonly EntryStore _store;
        private readonly VaultBackupService _backup;
        private bool _busy;
        private bool _backupBusy;
        private bool _loadingAutoLock;

        /// <summary>导入成功后为 true，调用方据此把程序切回锁定状态。</summary>
        public bool ImportCompleted { get; private set; }

        public SettingsWindow(EntryStore store)
        {
            _store = store;
            _backup = new VaultBackupService(store);
            InitializeComponent();
            Loaded += SettingsWindow_Loaded;
        }

        private async void SettingsWindow_Loaded(object sender, RoutedEventArgs e)
        {
            RequireCheck.IsChecked = _store.GetDeleteRequiresVerification();
            LoadAutoLockSetting();

            // 开关始终可交互：开启（提高保护）任何情况下都不需要验证；
            // Windows Hello 不可用时只有“关闭”会被阻止并给出提示。
            if (!await HelloAuthService.IsAvailableAsync())
            {
                UnavailableText.Visibility = Visibility.Visible;
            }
        }

        private async void RequireCheck_Click(object sender, RoutedEventArgs e)
        {
            if (_busy)
            {
                // 并发保护：以数据库状态为准恢复界面
                RequireCheck.IsChecked = _store.GetDeleteRequiresVerification();
                return;
            }

            var current = _store.GetDeleteRequiresVerification();
            var target = RequireCheck.IsChecked == true;

            // 关键：验证完成前界面必须保持原状态。CheckBox 的勾选在 Click 事件前已被翻转，
            // 这里在第一次 await 之前立刻按数据库真实值还原，避免“先变化再弹窗”的闪烁，
            // 同时保证验证未通过时界面不会提前呈现为已降低保护。
            RequireCheck.IsChecked = current;
            if (target == current)
            {
                return;
            }

            // 打开开关 = 提高保护等级，不需要验证，直接生效并写库。
            // 关闭开关 = 降低保护等级，必须先通过 Windows Hello。
            if (target)
            {
                try
                {
                    _store.SetDeleteRequiresVerification(true);
                    RequireCheck.IsChecked = true;
                    StatusText.Text = "设置已保存：每次删除都会验证身份。";
                    Log.Info("删除验证设置已开启");
                }
                catch (StorageException ex)
                {
                    Log.Error("保存删除验证设置失败", ex);
                    RequireCheck.IsChecked = _store.GetDeleteRequiresVerification();
                    StatusText.Text = "保存失败，设置未更改。";
                    MessageBox.Show(this, ex.Message, "保存失败", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                catch (Exception ex)
                {
                    Log.Error("开启删除验证设置异常", ex);
                    RequireCheck.IsChecked = _store.GetDeleteRequiresVerification();
                    StatusText.Text = "保存失败，设置未更改。";
                }
                return;
            }

            _busy = true;
            RequireCheck.IsEnabled = false;
            StatusText.Text = "正在验证身份…";

            try
            {
                if (!await HelloAuthService.IsAvailableAsync())
                {
                    UnavailableText.Visibility = Visibility.Visible;
                    RequireCheck.IsChecked = true;
                    StatusText.Text = "Windows Hello 不可用，无法完成验证，保护仍保持开启。";
                    Log.Info("关闭删除验证设置被阻止：Windows Hello 不可用");
                    return;
                }

                // 验证前先在主窗口内盖上不透明遮挡，避免验证窗口被拖动时露出条目内容
                var mainWindow = Owner as MainWindow;
                var verified = mainWindow is not null
                    ? await mainWindow.VerifyWithShieldAsync("请验证身份以更改删除验证设置")
                    : await HelloAuthService.VerifyAsync("请验证身份以更改删除验证设置");
                if (!verified)
                {
                    // 验证取消 / 关闭验证窗口 / 未通过：一律保持开启，且不写库
                    RequireCheck.IsChecked = true;
                    StatusText.Text = "验证未通过或已取消，保护仍保持开启，设置未更改。";
                    Log.Info("关闭删除验证设置：身份验证未通过或被取消");
                    return;
                }

                _store.SetDeleteRequiresVerification(false);
                RequireCheck.IsChecked = false;
                StatusText.Text = "设置已保存：删除时不再验证身份。";
                Log.Info("删除验证设置已关闭");
            }
            catch (StorageException ex)
            {
                Log.Error("保存删除验证设置失败", ex);
                RequireCheck.IsChecked = _store.GetDeleteRequiresVerification();
                StatusText.Text = "保存失败，设置未更改。";
                MessageBox.Show(this, ex.Message, "保存失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                Log.Error("关闭删除验证设置异常", ex);
                RequireCheck.IsChecked = _store.GetDeleteRequiresVerification();
                StatusText.Text = "验证过程出现异常，设置未更改。";
            }
            finally
            {
                _busy = false;
                RequireCheck.IsEnabled = true;
            }
        }

        private void LoadAutoLockSetting()
        {
            var minutes = _store.GetAutoLockMinutes();
            var index = Array.IndexOf(AutoLockMinutesOptions, minutes);
            if (index < 0)
            {
                index = 1;
            }

            _loadingAutoLock = true;
            AutoLockCombo.SelectedIndex = index;
            _loadingAutoLock = false;
            AutoLockWarning.Visibility = minutes == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void AutoLockCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loadingAutoLock)
            {
                return;
            }

            var index = AutoLockCombo.SelectedIndex;
            if (index < 0 || index >= AutoLockMinutesOptions.Length)
            {
                return;
            }

            var minutes = AutoLockMinutesOptions[index];
            try
            {
                _store.SetAutoLockMinutes(minutes);
                AutoLockWarning.Visibility = minutes == 0 ? Visibility.Visible : Visibility.Collapsed;
                StatusText.Text = minutes == 0
                    ? "设置已保存：已关闭闲置自动锁定。"
                    : $"设置已保存：闲置 {minutes} 分钟后自动锁定。";
                Log.Info(minutes == 0 ? "已关闭闲置自动锁定" : $"闲置自动锁定设置为 {minutes} 分钟");
            }
            catch (StorageException ex)
            {
                Log.Error("保存自动锁定设置失败", ex);
                MessageBox.Show(this, ex.Message, "保存失败", MessageBoxButton.OK, MessageBoxImage.Error);
                LoadAutoLockSetting();
            }
            catch (Exception ex)
            {
                Log.Error("保存自动锁定设置异常", ex);
                LoadAutoLockSetting();
            }
        }

        private async void ExportBackupButton_Click(object sender, RoutedEventArgs e)
        {
            if (_backupBusy)
            {
                return;
            }

            _backupBusy = true;
            ExportBackupButton.IsEnabled = false;
            ImportBackupButton.IsEnabled = false;
            try
            {
                // 先过 Windows Hello（沿用主窗口的遮挡层）
                var mainWindow = Owner as MainWindow;
                var verified = mainWindow is not null
                    ? await mainWindow.VerifyWithShieldAsync("请验证身份以导出加密备份")
                    : await HelloAuthService.VerifyAsync("请验证身份以导出加密备份");
                if (!verified)
                {
                    StatusText.Text = "验证未通过，导出已取消。";
                    Log.Info("导出加密备份：身份验证未通过");
                    return;
                }

                var passwordDialog = new ExportBackupWindow { Owner = this };
                if (passwordDialog.ShowDialog() != true)
                {
                    StatusText.Text = "已取消导出。";
                    return;
                }

                var masterPassword = passwordDialog.TakeMasterPassword();
                if (string.IsNullOrEmpty(masterPassword))
                {
                    StatusText.Text = "已取消导出。";
                    return;
                }

                byte[] encrypted;
                try
                {
                    encrypted = _backup.CreateBackup(masterPassword);
                }
                catch (BackupException ex)
                {
                    StatusText.Text = ex.Message;
                    MessageBox.Show(this, ex.Message, "导出失败", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                finally
                {
                    masterPassword = null;
                }

                var saveDialog = new SaveFileDialog
                {
                    Title = "保存加密备份",
                    Filter = "加密备份 (*.vault)|*.vault",
                    DefaultExt = "vault",
                    AddExtension = true,
                    OverwritePrompt = true,
                    FileName = $"LocalPasswordVault-{DateTime.Now:yyyyMMdd-HHmmss}.vault",
                };

                if (saveDialog.ShowDialog(this) != true)
                {
                    StatusText.Text = "已取消导出（未写入文件）。";
                    return;
                }

                try
                {
                    File.WriteAllBytes(saveDialog.FileName, encrypted);
                    StatusText.Text = "导出成功。";
                    MessageBox.Show(
                        this,
                        $"加密备份已保存到：\n{saveDialog.FileName}",
                        "导出成功",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    Log.Info("导出加密备份失败：无法写入所选位置");
                    StatusText.Text = "导出失败：无法写入所选位置。";
                    MessageBox.Show(
                        this,
                        "导出失败：无法写入所选位置，请检查磁盘空间与文件权限。",
                        "导出失败",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
            finally
            {
                _backupBusy = false;
                ExportBackupButton.IsEnabled = true;
                ImportBackupButton.IsEnabled = true;
            }
        }

        private void ImportBackupButton_Click(object sender, RoutedEventArgs e)
        {
            if (_backupBusy)
            {
                return;
            }

            _backupBusy = true;
            ExportBackupButton.IsEnabled = false;
            ImportBackupButton.IsEnabled = false;
            try
            {
                var dialog = new ImportBackupWindow { Owner = this };
                if (dialog.ShowDialog() != true)
                {
                    StatusText.Text = "已取消导入。";
                    return;
                }

                var filePath = dialog.SelectedFilePath;
                var masterPassword = dialog.TakeMasterPassword();
                if (string.IsNullOrEmpty(filePath) || string.IsNullOrEmpty(masterPassword))
                {
                    StatusText.Text = "已取消导入。";
                    return;
                }

                // 能解开 AES-GCM 载荷并输出有效 JSON，就已证明持有主密码且文件未被篡改，
                // 因此这里不再单独调用 Windows Hello，避免重复验证拖慢流程。
                BackupPayload payload;
                try
                {
                    payload = _backup.ReadBackup(filePath, masterPassword);
                }
                catch (BackupDecryptException)
                {
                    StatusText.Text = "校验失败：密码错误或文件损坏。";
                    MessageBox.Show(
                        this,
                        "密码错误或文件损坏。",
                        "导入失败",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }
                catch (BackupFormatException ex)
                {
                    StatusText.Text = ex.Message;
                    MessageBox.Show(this, ex.Message, "无法导入", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                catch (BackupException ex)
                {
                    StatusText.Text = ex.Message;
                    MessageBox.Show(this, ex.Message, "无法导入", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                finally
                {
                    masterPassword = null;
                }

                var count = payload.Entries?.Count ?? 0;

                // 唯一一次确认：条目数量、覆盖后果、自动备份说明；结构版本差异也合并在这里，
                // 不再弹出连环确认框。
                var versionNote = payload.SchemaVersion < VaultBackupService.CurrentSchemaVersion
                    ? $"\n该备份的结构版本较旧（{payload.SchemaVersion}），将按兼容方式导入。"
                    : string.Empty;
                var confirmed = MessageBox.Show(
                    this,
                    $"即将导入 {count} 条条目。\n\n当前密码库会被完全替换。\n覆盖前会自动备份到数据库同目录（passwords.db.<时间戳>.bak），需要时可手动恢复。{versionNote}\n\n是否继续？",
                    "确认导入",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No);
                if (confirmed != MessageBoxResult.Yes)
                {
                    StatusText.Text = "已取消导入，当前密码库未修改。";
                    return;
                }

                string backupPath;
                try
                {
                    backupPath = _backup.ReplaceDatabase(payload);
                }
                catch (BackupException ex)
                {
                    StatusText.Text = ex.Message;
                    MessageBox.Show(this, ex.Message, "导入失败", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                MessageBox.Show(
                    this,
                    $"导入成功，共 {count} 条。建议重启软件。\n\n覆盖前已自动备份当前数据库：\n{backupPath}\n\n程序将回到锁定状态，重新验证后会加载导入的数据。",
                    "导入成功",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                ImportCompleted = true;
                Close();
            }
            finally
            {
                _backupBusy = false;
                ExportBackupButton.IsEnabled = true;
                ImportBackupButton.IsEnabled = true;
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    }
}
