using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Streams;
using clip.Core.Models;
using System.Text.RegularExpressions;

namespace clip.Core.Clipboard;

public static class ClipboardReader
{
    public static async Task<ClipboardItemDraft?> ReadAsync()
    {
        // 尝试读取剪贴板，最多重试 5 次
        for (int i = 0; i < 5; i++)
        {
            try
            {
                var dataPackageView = Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
                
                // 1. 优先处理图片 (Bitmap) - 解决“图片变png文件”问题
                if (dataPackageView.Contains(StandardDataFormats.Bitmap))
                {
                    var bitmapStreamRef = await dataPackageView.GetBitmapAsync();
                    using var stream = await bitmapStreamRef.OpenReadAsync();

                    const ulong maxBytes = 20UL * 1024UL * 1024UL; // 提到 20MB 容忍度
                    if (stream.Size > maxBytes)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Clipboard] 图片过大({stream.Size} bytes)，跳过解析");
                        return null;
                    }

                    var buffer = new byte[(int)stream.Size];
                    uint readTotal = 0;
                    while (readTotal < buffer.Length)
                    {
                        uint toRead = (uint)Math.Min(64 * 1024, buffer.Length - readTotal);
                        var chunk = new Windows.Storage.Streams.Buffer(toRead);
                        var read = await stream.ReadAsync(chunk, toRead, InputStreamOptions.None);
                        if (read.Length == 0) break;
                        read.ToArray().CopyTo(buffer, (int)readTotal);
                        readTotal += read.Length;
                    }

                    return new ClipboardItemDraft(
                        ClipboardItemType.Image,
                        DateTimeOffset.UtcNow,
                        buffer.Length,
                        ImagePng: buffer
                    );
                }

                // 2. 处理文件列表 (CF_HDROP)
                if (dataPackageView.Contains(StandardDataFormats.StorageItems))
                {
                    var items = await dataPackageView.GetStorageItemsAsync();
                    if (items.Count > 0)
                    {
                        var paths = items.Select(i => i.Path).ToList();
                        return new ClipboardItemDraft(
                            ClipboardItemType.Files,
                            DateTimeOffset.UtcNow,
                            paths.Count * 256,
                            FilePaths: paths
                        );
                    }
                }

                // 3. 处理文本
                if (dataPackageView.Contains(StandardDataFormats.Text))
                {
                    var text = await dataPackageView.GetTextAsync();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        var sensitiveType = DetectSensitiveType(text);
                        return new ClipboardItemDraft(
                            ClipboardItemType.Text,
                            DateTimeOffset.UtcNow,
                            text.Length * 2,
                            Text: text,
                            IsSensitive: sensitiveType != SensitiveType.None,
                            SensitiveType: sensitiveType
                        );
                    }
                }

                // 读取成功或无内容，退出循环
                break;
            }
            catch (Exception ex)
            {
                // 如果是最后一次尝试，记录错误并返回 null
                if (i == 4)
                {
                    System.Diagnostics.Debug.WriteLine($"读取剪贴板失败(重试无效): {ex.Message}");
                    return null;
                }
                
                // 等待后重试 (100ms)
                await Task.Delay(100);
            }
        }

        return null;
    }

    // ── 敏感内容检测 ──

    /// <summary>
    /// 综合检测敏感内容类型
    /// </summary>
    public static SensitiveType DetectSensitiveType(string? text)
    {
        if (string.IsNullOrEmpty(text)) return SensitiveType.None;
        if (text.Length > 5000) return SensitiveType.None; // 超长文本不太可能是密钥

        // 1. 私钥 (最优先，误判率最低)
        if (Regex.IsMatch(text, @"-----BEGIN\s+(RSA|EC|OPENSSH|DSA|ENCRYPTED)?\s*PRIVATE KEY-----"))
            return SensitiveType.PrivateKey;

        // 2. 数据库连接串
        if (Regex.IsMatch(text, @"(Server|Data Source|Host)\s*=.+(Password|Pwd)\s*=", RegexOptions.IgnoreCase))
            return SensitiveType.ConnectionString;
        if (Regex.IsMatch(text, @"(mysql|postgres|mongodb(\+srv)?|redis)://[^:]+:[^@]+@", RegexOptions.IgnoreCase))
            return SensitiveType.ConnectionString;

        // 3. API Key / Token
        // OpenAI
        if (Regex.IsMatch(text, @"sk-[a-zA-Z0-9]{20,}"))
            return SensitiveType.ApiKey;
        // GitHub PAT
        if (Regex.IsMatch(text, @"gh[ps]_[a-zA-Z0-9]{20,}"))
            return SensitiveType.ApiKey;
        // AWS Access Key
        if (Regex.IsMatch(text, @"AKIA[0-9A-Z]{16}"))
            return SensitiveType.ApiKey;
        // JWT
        if (Regex.IsMatch(text, @"^eyJ[a-zA-Z0-9\-_]+\.eyJ[a-zA-Z0-9\-_]+\.[a-zA-Z0-9\-_]+$"))
            return SensitiveType.ApiKey;
        // Bearer Token
        if (Regex.IsMatch(text, @"Bearer\s+[a-zA-Z0-9\-_.]+", RegexOptions.IgnoreCase))
            return SensitiveType.ApiKey;
        // Generic long hex/base64 tokens (40+ chars, no spaces)
        if (Regex.IsMatch(text, @"^[a-zA-Z0-9\-_]{40,}$") && text.Length <= 200)
            return SensitiveType.ApiKey;

        // 4. 账号+密码组合 (中文/英文格式)
        // 中文：账号：xxx 密码：xxx  /  用户名：xxx 口令：xxx
        if (Regex.IsMatch(text, @"(账号|用户名|用户|帐号|邮箱|手机)\s*[：:]\s*.+[\r\n\s]+(密码|口令|pass|pwd)\s*[：:]\s*.+", RegexOptions.IgnoreCase))
            return SensitiveType.Credential;
        // 英文：username: xxx password: xxx  /  user=xxx&pass=xxx
        if (Regex.IsMatch(text, @"(user(name)?|login|email|account)\s*[=:]\s*.+[\r\n\s&]+(pass(word)?|pwd|secret)\s*[=:]\s*.+", RegexOptions.IgnoreCase))
            return SensitiveType.Credential;

        // 5. 独立密码 (复杂字符串：大小写+数字+符号, 8-64, 无空格)
        if (text.Length <= 64 && !text.Contains(' ') && !text.Contains('\n'))
        {
            if (Regex.IsMatch(text, @"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[\W_]).{8,64}$"))
                return SensitiveType.Password;
        }

        return SensitiveType.None;
    }
}
