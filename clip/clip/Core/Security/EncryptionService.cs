using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace clip.Core.Security;

public static class EncryptionService
{
    private static readonly byte[] Salt = Encoding.UTF8.GetBytes("WinClipboard_Salt_2026");
    private const int Iterations = 10000;

    // 简单起见，这里使用一个固定的 Key 派生。
    // 在生产环境中，建议结合机器硬件 ID 生成 Key。
    private static byte[] GetKey()
    {
        using var rfc2898 = new Rfc2898DeriveBytes("App_Master_Key_Secret", Salt, Iterations, HashAlgorithmName.SHA256);
        return rfc2898.GetBytes(32); // 256 bits
    }

    public static string Encrypt(string plainText)
    {
        if (string.IsNullOrEmpty(plainText)) return plainText;

        using var aes = Aes.Create();
        aes.Key = GetKey();
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
        using var ms = new MemoryStream();
        
        // 写入 IV
        ms.Write(aes.IV, 0, aes.IV.Length);

        using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
        using (var sw = new StreamWriter(cs))
        {
            sw.Write(plainText);
        }

        return Convert.ToBase64String(ms.ToArray());
    }

    public static string Decrypt(string cipherText)
    {
        if (string.IsNullOrEmpty(cipherText)) return cipherText;

        try
        {
            byte[] fullCipher = Convert.FromBase64String(cipherText);

            using var aes = Aes.Create();
            aes.Key = GetKey();

            byte[] iv = new byte[aes.BlockSize / 8];
            byte[] cipher = new byte[fullCipher.Length - iv.Length];

            Buffer.BlockCopy(fullCipher, 0, iv, 0, iv.Length);
            Buffer.BlockCopy(fullCipher, iv.Length, cipher, 0, cipher.Length);

            aes.IV = iv;

            using var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
            using var ms = new MemoryStream(cipher);
            using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
            using var sr = new StreamReader(cs);

            return sr.ReadToEnd();
        }
        catch
        {
            // 如果解密失败，可能内容未加密或密钥不匹配，返回原样以防数据丢失
            return cipherText;
        }
    }
}
