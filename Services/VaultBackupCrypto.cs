using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace LocalPasswordVault.Services
{
    /// <summary>
    /// 备份文件的加密封装（与系统账户无关，可用主密码跨机恢复）。
    ///
    /// 文件字节布局（全部小端，除魔数为 ASCII）：
    ///   0..4    "VAULT" 魔数（5 字节）
    ///   5..6    格式版本 uint16（当前 1）
    ///   7..8    保留 uint16 = 0
    ///   9..12   PBKDF2 迭代次数 uint32
    ///   13..44  salt 32 字节（随机）
    ///   45..56  GCM nonce 12 字节（随机）
    ///   57..N-16 密文（AES-256-GCM）
    ///   最后 16 字节 GCM tag
    /// 前 45 字节（魔数 ~ salt）作为 GCM 的附加认证数据，防止 KDF 参数被篡改。
    /// </summary>
    public static class VaultBackupCrypto
    {
        public const int FormatVersion = 1;
        public const int KdfIterations = 210_000;
        public const int SaltLength = 32;
        public const int NonceLength = 12;
        public const int TagLength = 16;

        // 魔数(5) + 版本(2) + 保留(2) + 迭代次数(4) + salt(32)
        private const int HeaderLength = 45;
        private const int NonceOffset = HeaderLength;
        private const int CipherOffset = NonceOffset + NonceLength;

        private static readonly byte[] Magic = { (byte)'V', (byte)'A', (byte)'U', (byte)'L', (byte)'T' };

        /// <summary>旧版本备份曾使用的魔数，仅用于给出明确提示，不参与任何解密或兼容处理。</summary>
        private static readonly byte[] LegacyMagic = { (byte)'H', (byte)'B', (byte)'T', (byte)'V' };

        /// <summary>用主密码把明文载荷加密为备份文件字节。</summary>
        public static byte[] Encrypt(byte[] plaintext, string masterPassword)
        {
            var salt = RandomNumberGenerator.GetBytes(SaltLength);
            var nonce = RandomNumberGenerator.GetBytes(NonceLength);
            var header = BuildHeader(salt, KdfIterations);
            var key = DeriveKey(masterPassword, salt, KdfIterations);

            try
            {
                var cipher = new byte[plaintext.Length];
                var tag = new byte[TagLength];

                using (var aes = new AesGcm(key, TagLength))
                {
                    aes.Encrypt(nonce, plaintext, cipher, tag, header);
                }

                var result = new byte[CipherOffset + cipher.Length + TagLength];
                Buffer.BlockCopy(header, 0, result, 0, header.Length);
                Buffer.BlockCopy(nonce, 0, result, NonceOffset, nonce.Length);
                Buffer.BlockCopy(cipher, 0, result, CipherOffset, cipher.Length);
                Buffer.BlockCopy(tag, 0, result, CipherOffset + cipher.Length, tag.Length);
                return result;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
            }
        }

        /// <summary>
        /// 用主密码解出备份载荷。格式错误抛 <see cref="BackupFormatException"/>，
        /// 认证失败（密码错误或内容被改动）统一抛 <see cref="BackupDecryptException"/>。
        /// </summary>
        public static byte[] Decrypt(byte[] fileBytes, string masterPassword)
        {
            if (fileBytes.Length < Magic.Length)
            {
                throw new BackupFormatException("文件不是有效的加密备份，或已损坏。");
            }

            for (var i = 0; i < Magic.Length; i++)
            {
                if (fileBytes[i] != Magic[i])
                {
                    throw new BackupFormatException(
                        HasLegacyMagic(fileBytes)
                            ? "这是旧版本备份格式，当前版本不再支持，请用新版重新导出。"
                            : "所选文件不是本地密码库的加密备份。");
                }
            }

            if (fileBytes.Length < CipherOffset + TagLength)
            {
                throw new BackupFormatException("文件不是有效的加密备份，或已损坏。");
            }

            var version = BinaryPrimitives.ReadUInt16LittleEndian(fileBytes.AsSpan(5, 2));
            if (version != FormatVersion)
            {
                throw new BackupFormatException(
                    version > FormatVersion
                        ? $"该备份的文件格式版本较新（{version}），当前程序仅支持 {FormatVersion}，请升级程序后再导入。"
                        : $"该备份的文件格式版本过旧（{version}），当前程序无法读取。");
            }

            var iterations = BinaryPrimitives.ReadUInt32LittleEndian(fileBytes.AsSpan(9, 4));
            if (iterations < 10_000 || iterations > 2_000_000)
            {
                throw new BackupFormatException("备份文件的密钥派生参数异常，文件可能已损坏。");
            }

            var salt = fileBytes.AsSpan(13, SaltLength).ToArray();
            var header = fileBytes.AsSpan(0, HeaderLength).ToArray();
            var nonce = fileBytes.AsSpan(NonceOffset, NonceLength).ToArray();
            var cipherLength = fileBytes.Length - CipherOffset - TagLength;
            var cipher = fileBytes.AsSpan(CipherOffset, cipherLength).ToArray();
            var tag = fileBytes.AsSpan(CipherOffset + cipherLength, TagLength).ToArray();

            var key = DeriveKey(masterPassword, salt, (int)iterations);
            var plaintext = new byte[cipherLength];

            try
            {
                using var aes = new AesGcm(key, TagLength);
                aes.Decrypt(nonce, cipher, tag, plaintext, header);
                return plaintext;
            }
            catch (CryptographicException)
            {
                // 不区分“密码错误”和“文件被篡改”，避免泄露信息
                throw new BackupDecryptException("密码错误或文件损坏。");
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
            }
        }

        /// <summary>只做字节比对以便给出“旧格式”提示，不会尝试解密旧文件。</summary>
        private static bool HasLegacyMagic(byte[] bytes)
        {
            if (bytes.Length < LegacyMagic.Length)
            {
                return false;
            }

            for (var i = 0; i < LegacyMagic.Length; i++)
            {
                if (bytes[i] != LegacyMagic[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static byte[] BuildHeader(byte[] salt, int iterations)
        {
            var header = new byte[HeaderLength];
            Buffer.BlockCopy(Magic, 0, header, 0, Magic.Length);
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(5, 2), (ushort)FormatVersion);
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(7, 2), (ushort)0);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(9, 4), (uint)iterations);
            Buffer.BlockCopy(salt, 0, header, 13, SaltLength);
            return header;
        }

        private static byte[] DeriveKey(string masterPassword, byte[] salt, int iterations)
        {
            // PBKDF2-HMAC-SHA256，派生 256 位密钥
            var passwordBytes = Encoding.UTF8.GetBytes(masterPassword);
            try
            {
                return Rfc2898DeriveBytes.Pbkdf2(passwordBytes, salt, iterations, HashAlgorithmName.SHA256, 32);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(passwordBytes);
            }
        }
    }

    /// <summary>备份文件结构 / 版本问题（非密码问题）。</summary>
    public sealed class BackupFormatException : Exception
    {
        public BackupFormatException(string message) : base(message)
        {
        }
    }

    /// <summary>GCM 认证失败：密码错误或文件损坏，消息刻意不区分二者。</summary>
    public sealed class BackupDecryptException : Exception
    {
        public BackupDecryptException(string message) : base(message)
        {
        }
    }
}
