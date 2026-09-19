namespace JoinCode.CodeIndex;

/// <summary>
/// 哈希工具类 — 提供 SHA256 内容哈希计算与文件读取+哈希组合操作
/// </summary>
internal static class HashUtility {
    /// <summary>
    /// 计算字符串内容的 SHA256 哈希 — 返回十六进制字符串
    /// </summary>
    /// <param name="content">待哈希的文本内容</param>
    /// <returns>大写十六进制哈希字符串</returns>
    internal static string ComputeContentHash(string content) {
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        var hashBytes = System.Security.Cryptography.SHA256.HashData(bytes);
        return Convert.ToHexString(hashBytes);
    }

    /// <summary>
    /// 计算 UTF-8 字节序列的 SHA256 哈希 — 返回十六进制字符串
    /// </summary>
    /// <param name="utf8Bytes">待哈希的 UTF-8 字节序列</param>
    /// <returns>大写十六进制哈希字符串</returns>
    internal static string ComputeContentHash(ReadOnlySpan<byte> utf8Bytes) {
        var hashBytes = System.Security.Cryptography.SHA256.HashData(utf8Bytes);
        return Convert.ToHexString(hashBytes);
    }

    /// <summary>
    /// 读取文件内容并计算哈希 — 返回内容与哈希的元组
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="fs">文件系统抽象</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>文件内容与哈希的元组</returns>
    internal static async Task<(string Content, string Hash)> ReadFileAndComputeHashAsync(string filePath, IFileSystem fs, CancellationToken ct) {
        var content = await fs.ReadAllTextAsync(filePath, ct).ConfigureAwait(false);
        var hash = ComputeContentHash(content);
        return (content, hash);
    }
}