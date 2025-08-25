using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Serilog;

namespace PRC_API_Worker
{
    public static class Encryption
    {
        private static string EncryptionKey
        {
            get
            {
                string? key = Environment.GetEnvironmentVariable("ENCRYPTION_KEY");
                if (string.IsNullOrEmpty(key))
                {
                    throw new InvalidOperationException("ENCRYPTION_KEY environment variable is not set.");
                }
                return key;
            }
        }

        public static string EncryptApiKey(string apiKey)
        {
            using var aes = Aes.Create();
            aes.Key = Encoding.UTF8.GetBytes(EncryptionKey);
            aes.GenerateIV();
            var iv = aes.IV;

            using var encryptor = aes.CreateEncryptor(aes.Key, iv);
            using var ms = new MemoryStream();
            ms.Write(iv, 0, iv.Length);
            using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
            using (var sw = new StreamWriter(cs))
            {
                sw.Write(apiKey);
            }
            return Convert.ToBase64String(ms.ToArray());
        }

        public static string DecryptApiKey(string encryptedApiKey)
        {
            var fullCipher = Convert.FromBase64String(encryptedApiKey);

            using var aes = Aes.Create();
            aes.Key = Encoding.UTF8.GetBytes(EncryptionKey);
            var iv = new byte[aes.BlockSize / 8];
            Array.Copy(fullCipher, iv, iv.Length);

            using var decryptor = aes.CreateDecryptor(aes.Key, iv);
            using var ms = new MemoryStream(fullCipher, iv.Length, fullCipher.Length - iv.Length);
            using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
            using var sr = new StreamReader(cs);
            return sr.ReadToEnd();
        }

        public static string HashString(string input)
        {
            byte[] inputBytes = Encoding.UTF8.GetBytes(input);
            byte[] hashBytes = SHA256.HashData(inputBytes);

            StringBuilder sb = new();
            foreach (byte b in hashBytes)
            {
                sb.Append(b.ToString("x2"));
            }

            return sb.ToString();
        }
    }
}
