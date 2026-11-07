using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using LocalPasswordVault.Models;

namespace LocalPasswordVault.Services
{
    /// <summary>
    /// 加密备份的导出 / 导入编排。
    ///
    /// 重要：DPAPI 密文与本机 Windows 账户绑定，直接复制 SQLite 字节无法在其他电脑解密，
    /// 因此导出时先通过 EntryStore 解出条目明文，序列化为与机器无关的载荷，再用主密码加密；
    /// 导入时解密载荷，并用本机 DPAPI 重新加密后写入新数据库。
    /// </summary>
    public sealed class VaultBackupService
    {
        /// <summary>备份载荷内的结构版本。升级数据结构时递增。</summary>
        public const int CurrentSchemaVersion = 1;

        private const string AppName = "LocalPasswordVault";

        private readonly EntryStore _store;

        public VaultBackupService(EntryStore store)
        {
            _store = store;
        }

        /// <summary>把当前密码库导出为加密备份字节。失败抛 <see cref="BackupException"/>。</summary>
        public byte[] CreateBackup(string masterPassword)
        {
            BackupPayload payload;
            try
            {
                payload = BuildPayload();
            }
            catch (EntryDecryptionException ex)
            {
                Log.Info("导出加密备份失败：存在无法解密的条目");
                throw new BackupException(
                    "导出失败：有条目无法解密（可能来自其他 Windows 账户），请先确认本机可以正常查看所有条目。",
                    ex);
            }
            catch (StorageException ex)
            {
                Log.Info("导出加密备份失败：无法读取本地数据库");
                throw new BackupException("导出失败：无法读取本地数据库，请重试。", ex);
            }

            var json = JsonSerializer.SerializeToUtf8Bytes(payload);
            try
            {
                var result = VaultBackupCrypto.Encrypt(json, masterPassword);
                Log.Info($"导出加密备份成功（条目数：{payload.Entries!.Count}）");
                return result;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(json);
            }
        }

        /// <summary>
        /// 读取并解密备份，返回载荷。密码错误 / 文件损坏抛 <see cref="BackupDecryptException"/>，
        /// 结构或版本问题抛 <see cref="BackupFormatException"/> 或 <see cref="BackupException"/>。
        /// </summary>
        public BackupPayload ReadBackup(string filePath, string masterPassword)
        {
            byte[] fileBytes;
            try
            {
                fileBytes = File.ReadAllBytes(filePath);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
            {
                Log.Info("导入校验失败：无法读取备份文件");
                throw new BackupException("无法读取所选备份文件，请检查文件是否存在且可访问。", ex);
            }

            byte[]? json = null;
            try
            {
                json = VaultBackupCrypto.Decrypt(fileBytes, masterPassword);

                BackupPayload? payload;
                try
                {
                    payload = JsonSerializer.Deserialize<BackupPayload>(json);
                }
                catch (JsonException)
                {
                    throw new BackupFormatException("备份文件内容无效，可能已损坏。");
                }

                if (payload is null || payload.Entries is null)
                {
                    throw new BackupFormatException("备份文件内容无效，可能已损坏。");
                }

                if (payload.SchemaVersion > CurrentSchemaVersion)
                {
                    throw new BackupFormatException(
                        $"该备份由更新版本的程序创建（数据库结构版本 {payload.SchemaVersion}），当前程序仅支持 {CurrentSchemaVersion}，请升级程序后再导入。");
                }

                if (payload.App is null || payload.App.Length == 0)
                {
                    throw new BackupFormatException("备份文件内容无效，可能已损坏。");
                }

                Log.Info($"导入校验通过（结构版本：{payload.SchemaVersion}，条目数：{payload.Entries.Count}）");
                return payload;
            }
            catch (BackupDecryptException)
            {
                Log.Info("导入校验失败：密码错误或文件损坏");
                throw;
            }
            catch (BackupFormatException)
            {
                Log.Info("导入校验失败：备份格式不匹配");
                throw;
            }
            finally
            {
                if (json is not null)
                {
                    CryptographicOperations.ZeroMemory(json);
                }
            }
        }

        /// <summary>
        /// 把载荷写入新数据库并替换当前库。替换前会在同目录生成带时间戳的 .bak；
        /// 覆盖失败时尝试自动回滚。返回备份文件路径。
        /// </summary>
        public string ReplaceDatabase(BackupPayload payload)
        {
            var databasePath = _store.DatabasePath;
            var directory = Path.GetDirectoryName(databasePath);
            if (string.IsNullOrEmpty(directory))
            {
                throw new BackupException("无法确定数据库所在目录，导入已取消，原数据库未修改。");
            }

            var tempPath = Path.Combine(directory, $"passwords.import-{Guid.NewGuid():N}.tmp");
            string? backupPath = null;

            try
            {
                var tempStore = new EntryStore(tempPath);
                try
                {
                    tempStore.Initialize();
                    foreach (var entry in payload.Entries!)
                    {
                        var name = string.IsNullOrWhiteSpace(entry.Name) ? "未命名" : entry.Name!.Trim();
                        tempStore.Create(name, entry.Username ?? string.Empty, entry.Password ?? string.Empty, entry.Extra ?? string.Empty);
                    }

                    // 导入只替换条目，保留本机的保护偏好设置
                    tempStore.SetDeleteRequiresVerification(_store.GetDeleteRequiresVerification());
                    tempStore.SetAutoLockMinutes(_store.GetAutoLockMinutes());
                }
                catch (Exception ex) when (ex is StorageException || ex is ArgumentException)
                {
                    Log.Info("导入失败：无法生成新数据库");
                    throw new BackupException("导入失败：无法生成新的数据库文件，原数据库未修改。", ex);
                }

                backupPath = CreateBackupCopy(databasePath);

                try
                {
                    // 同目录替换，优先用原子替换；个别环境不支持时退回移动覆盖
                    try
                    {
                        File.Replace(tempPath, databasePath, null);
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is PlatformNotSupportedException)
                    {
                        File.Move(tempPath, databasePath, true);
                    }
                }
                catch (Exception ex)
                {
                    var restored = TryRestore(backupPath, databasePath);
                    Log.Info("导入失败：无法覆盖数据库");
                    throw new BackupException(
                        restored
                            ? "导入失败：无法覆盖数据库，已自动从备份恢复原数据库。"
                            : $"导入失败：无法覆盖数据库，且自动恢复失败。原数据库备份位于：{backupPath}",
                        ex);
                }

                Log.Info("导入成功");
                return backupPath;
            }
            finally
            {
                try
                {
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }
                catch
                {
                    // 临时文件清理失败不影响导入结果
                }
            }
        }

        private BackupPayload BuildPayload()
        {
            var entries = new List<BackupEntry>();
            foreach (var summary in _store.List())
            {
                var detail = _store.Get(summary.Id);
                entries.Add(new BackupEntry
                {
                    Name = detail.Name,
                    Username = detail.Username,
                    Password = detail.Password,
                    Extra = detail.Extra,
                });
            }

            return new BackupPayload
            {
                SchemaVersion = CurrentSchemaVersion,
                App = AppName,
                ExportedAt = DateTime.Now.ToString("o", CultureInfo.InvariantCulture),
                Entries = entries,
            };
        }

        private static string CreateBackupCopy(string databasePath)
        {
            var directory = Path.GetDirectoryName(databasePath) ?? string.Empty;
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            var candidate = Path.Combine(directory, $"passwords.db.{stamp}.bak");
            var index = 2;
            while (File.Exists(candidate))
            {
                candidate = Path.Combine(directory, $"passwords.db.{stamp}_{index}.bak");
                index++;
            }

            try
            {
                File.Copy(databasePath, candidate, false);
                Log.Info("导入前已备份当前数据库");
                return candidate;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Log.Info("导入取消：无法备份当前数据库");
                throw new BackupException("导入已取消：无法备份当前数据库，原数据库未修改。", ex);
            }
        }

        private static bool TryRestore(string? backupPath, string databasePath)
        {
            if (string.IsNullOrEmpty(backupPath) || !File.Exists(backupPath))
            {
                return false;
            }

            try
            {
                File.Copy(backupPath, databasePath, true);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>备份 / 导入的用户可见异常，消息不含敏感内容。</summary>
    public sealed class BackupException : Exception
    {
        public BackupException(string message, Exception? innerException = null)
            : base(message, innerException)
        {
        }
    }

    /// <summary>备份载荷（加密前的明文结构，只在内存中存在）。</summary>
    public sealed class BackupPayload
    {
        public int SchemaVersion { get; set; }
        public string? App { get; set; }
        public string? ExportedAt { get; set; }
        public List<BackupEntry>? Entries { get; set; }
    }

    public sealed class BackupEntry
    {
        public string? Name { get; set; }
        public string? Username { get; set; }
        public string? Password { get; set; }
        public string? Extra { get; set; }
    }
}
