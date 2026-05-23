using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace clip.Core.Storage;

/// <summary>
/// Blob directory management, SHA256 computation, image file write/delete/resolve.
/// </summary>
internal sealed class BlobStore
{
    public string BlobDir { get; }

    public BlobStore(string blobDir)
    {
        BlobDir = blobDir;
        Directory.CreateDirectory(BlobDir);
    }

    public static string ComputeSha256(byte[] data)
    {
        var hash = SHA256.HashData(data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public async Task<string> WriteBlobAsync(byte[] data)
    {
        var fileName = $"{Guid.NewGuid():N}.png";
        await File.WriteAllBytesAsync(Path.Combine(BlobDir, fileName), data);
        return fileName;
    }

    public void DeleteBlob(string relativePath)
    {
        try { File.Delete(Path.Combine(BlobDir, relativePath)); } catch { }
    }

    public string? GetFullPath(string? relativePath)
    {
        if (string.IsNullOrEmpty(relativePath)) return null;
        var full = Path.Combine(BlobDir, relativePath);
        return File.Exists(full) ? full : null;
    }
}
