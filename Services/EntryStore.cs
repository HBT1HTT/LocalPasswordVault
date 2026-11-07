using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using LocalPasswordVault.Models;
using Microsoft.Data.Sqlite;

namespace LocalPasswordVault.Services
{
    /// <summary>
    /// 密码库存储服务：名称明文，账号 / 密码 / 附加信息 DPAPI 加密后以 BLOB 存库。
    /// 界面层只调用本服务，不直接接触 DPAPI。
    /// </summary>
    public sealed class EntryStore
    {
        private readonly string _databasePath;

        public EntryStore(string databasePath)
        {
            _databasePath = databasePath;
        }

        public static string DefaultDatabasePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LocalPasswordVault",
            "passwords.db");

        public string DatabasePath => _databasePath;

        /// <summary>首次运行自动建库建表；失败时不删除、不重建原文件。</summary>
        public void Initialize()
        {
            try
            {
                var directory = Path.GetDirectoryName(_databasePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                using var connection = OpenConnection();
                using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE IF NOT EXISTS Entries (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Name TEXT NOT NULL,
                        UsernameCipher BLOB NOT NULL,
                        PasswordCipher BLOB NOT NULL,
                        ExtraCipher BLOB NOT NULL,
                        CreatedAt TEXT NOT NULL,
                        UpdatedAt TEXT NOT NULL
                    );
                    """;
                command.ExecuteNonQuery();
            }
            catch (Exception ex) when (IsStorageFailure(ex))
            {
                Log.Error("初始化数据库失败", ex);
                throw new StorageException(
                    "无法打开本地数据库，文件可能已损坏或无法访问。为避免数据丢失，程序不会修改或重建该文件。",
                    ex);
            }
        }

        public IReadOnlyList<EntrySummary> List()
        {
            try
            {
                using var connection = OpenConnection();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT Id, Name FROM Entries ORDER BY Name COLLATE NOCASE, Id;";
                using var reader = command.ExecuteReader();

                var result = new List<EntrySummary>();
                while (reader.Read())
                {
                    result.Add(new EntrySummary(reader.GetInt64(0), reader.GetString(1)));
                }

                return result;
            }
            catch (Exception ex) when (IsStorageFailure(ex))
            {
                Log.Error("读取条目列表失败", ex);
                throw new StorageException("无法读取密码库数据，数据库文件可能已损坏。", ex);
            }
        }

        public EntryDetail Get(long id)
        {
            string name;
            byte[] usernameCipher;
            byte[] passwordCipher;
            byte[] extraCipher;

            try
            {
                using var connection = OpenConnection();
                using var command = connection.CreateCommand();
                command.CommandText = """
                    SELECT Name, UsernameCipher, PasswordCipher, ExtraCipher
                    FROM Entries WHERE Id = @id;
                    """;
                command.Parameters.AddWithValue("@id", id);

                using var reader = command.ExecuteReader();
                if (!reader.Read())
                {
                    throw new StorageException("该条目不存在，可能已被删除。");
                }

                name = reader.GetString(0);
                usernameCipher = (byte[])reader.GetValue(1);
                passwordCipher = (byte[])reader.GetValue(2);
                extraCipher = (byte[])reader.GetValue(3);
            }
            catch (Exception ex) when (IsStorageFailure(ex))
            {
                Log.Error("读取条目失败", ex);
                throw new StorageException("无法读取该条目，数据库文件可能已损坏。", ex);
            }

            try
            {
                return new EntryDetail(
                    id,
                    name,
                    DpapiProtector.Unprotect(usernameCipher),
                    DpapiProtector.Unprotect(passwordCipher),
                    DpapiProtector.Unprotect(extraCipher));
            }
            catch (Exception ex) when (ex is CryptographicException || ex is FormatException)
            {
                // 不向界面输出任何乱码或异常细节
                Log.Error("条目解密失败", ex);
                throw new EntryDecryptionException(ex);
            }
        }

        public long Create(string name, string username, string password, string extra)
        {
            try
            {
                using var connection = OpenConnection();
                using var command = connection.CreateCommand();
                command.CommandText = """
                    INSERT INTO Entries (Name, UsernameCipher, PasswordCipher, ExtraCipher, CreatedAt, UpdatedAt)
                    VALUES (@name, @username, @password, @extra, @createdAt, @updatedAt);
                    """;
                var now = DateTime.Now.ToString("o", CultureInfo.InvariantCulture);
                command.Parameters.AddWithValue("@name", name);
                command.Parameters.AddWithValue("@username", DpapiProtector.Protect(username));
                command.Parameters.AddWithValue("@password", DpapiProtector.Protect(password));
                command.Parameters.AddWithValue("@extra", DpapiProtector.Protect(extra));
                command.Parameters.AddWithValue("@createdAt", now);
                command.Parameters.AddWithValue("@updatedAt", now);
                command.ExecuteNonQuery();

                using var idCommand = connection.CreateCommand();
                idCommand.CommandText = "SELECT last_insert_rowid();";
                return (long)idCommand.ExecuteScalar()!;
            }
            catch (Exception ex) when (IsStorageFailure(ex) || ex is CryptographicException)
            {
                Log.Error("新增条目失败", ex);
                throw new StorageException("保存失败：无法写入本地数据库，请重试。", ex);
            }
        }

        public void Update(long id, string name, string username, string password, string extra)
        {
            try
            {
                using var connection = OpenConnection();
                using var command = connection.CreateCommand();
                command.CommandText = """
                    UPDATE Entries
                    SET Name = @name,
                        UsernameCipher = @username,
                        PasswordCipher = @password,
                        ExtraCipher = @extra,
                        UpdatedAt = @updatedAt
                    WHERE Id = @id;
                    """;
                command.Parameters.AddWithValue("@name", name);
                command.Parameters.AddWithValue("@username", DpapiProtector.Protect(username));
                command.Parameters.AddWithValue("@password", DpapiProtector.Protect(password));
                command.Parameters.AddWithValue("@extra", DpapiProtector.Protect(extra));
                command.Parameters.AddWithValue("@updatedAt", DateTime.Now.ToString("o", CultureInfo.InvariantCulture));
                command.Parameters.AddWithValue("@id", id);

                if (command.ExecuteNonQuery() == 0)
                {
                    throw new StorageException("该条目不存在，可能已被删除。");
                }
            }
            catch (Exception ex) when (IsStorageFailure(ex) || ex is CryptographicException)
            {
                Log.Error("更新条目失败", ex);
                throw new StorageException("保存失败：无法写入本地数据库，请重试。", ex);
            }
        }

        private SqliteConnection OpenConnection()
        {
            var connection = new SqliteConnection($"Data Source={_databasePath};Mode=ReadWriteCreate;Pooling=False");
            connection.Open();
            return connection;
        }

        private static bool IsStorageFailure(Exception ex) =>
            ex is SqliteException || ex is IOException || ex is UnauthorizedAccessException;
    }
}
