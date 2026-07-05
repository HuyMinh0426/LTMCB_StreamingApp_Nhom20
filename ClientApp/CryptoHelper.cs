using System;
using System.Security.Cryptography;
using System.Text;

namespace ClientApp
{
    public static class CryptoHelper
    {
        private static readonly byte[] Key = SHA256.HashData(
            Encoding.UTF8.GetBytes("MINHFLIX_SECRET_KEY_2026"));

        public static string Encrypt(string plainText)
        {
            using Aes aes = Aes.Create();
            aes.Key = Key;
            aes.GenerateIV();
            byte[] iv = aes.IV;

            using var encryptor = aes.CreateEncryptor();
            byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
            byte[] cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

            byte[] combined = new byte[iv.Length + cipherBytes.Length];
            Array.Copy(iv, 0, combined, 0, iv.Length);
            Array.Copy(cipherBytes, 0, combined, iv.Length, cipherBytes.Length);
            return Convert.ToBase64String(combined);
        }

        public static string Decrypt(string cipherTextBase64)
        {
            byte[] combined = Convert.FromBase64String(cipherTextBase64);

            using Aes aes = Aes.Create();
            aes.Key = Key;

            byte[] iv = new byte[16];
            Array.Copy(combined, 0, iv, 0, 16);
            aes.IV = iv;

            byte[] cipherBytes = new byte[combined.Length - 16];
            Array.Copy(combined, 16, cipherBytes, 0, cipherBytes.Length);

            using var decryptor = aes.CreateDecryptor();
            byte[] plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
            return Encoding.UTF8.GetString(plainBytes);
        }
    }
}