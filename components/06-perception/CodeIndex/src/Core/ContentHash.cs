namespace CodeIndex;

internal static class HashUtility
{
    internal static string ComputeContentHash(string content)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        var hashBytes = System.Security.Cryptography.SHA256.HashData(bytes);
        return Convert.ToHexString(hashBytes);
    }

    internal static string ComputeContentHash(ReadOnlySpan<byte> utf8Bytes)
    {
        var hashBytes = System.Security.Cryptography.SHA256.HashData(utf8Bytes);
        return Convert.ToHexString(hashBytes);
    }

    internal static async Task<(string Content, string Hash)> ReadFileAndComputeHashAsync(string filePath, IFileSystem fs, CancellationToken ct)
    {
        var content = await fs.ReadAllTextAsync(filePath, ct).ConfigureAwait(false);
        var hash = ComputeContentHash(content);
        return (content, hash);
    }
}
