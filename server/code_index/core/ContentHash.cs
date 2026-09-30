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
        ArgumentNullException.ThrowIfNull(content);
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
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(fs);
        var content = await fs.ReadAllTextAsync(filePath, ct).ConfigureAwait(false);
        var hash = ComputeContentHash(content);
        return (content, hash);
    }

    /// <summary>
    /// 用 mmap 读取大文件内容并计算哈希 — 零拷贝内存映射，避免 ReadAllTextAsync 的 byte[] 中间分配。
    /// <para>仅用于大文件（&gt;1MB），小文件用 ReadFileAndComputeHashAsync 更快（mmap 创建有固定开销）。</para>
    /// <para>用 ArrayPool 租借缓冲区，减少 GC 压力。</para>
    /// </summary>
    /// <param name="filePath">文件路径（必须是磁盘上的真实文件）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>文件内容与哈希的元组。</returns>
    internal static async Task<(string Content, string Hash)> ReadFileAndComputeHashMappedAsync(string filePath, CancellationToken ct) {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        using var mmf = MemoryMappedFile.CreateFromFile(filePath, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
        await using var vs = mmf.CreateViewStream(0, 0, MemoryMappedFileAccess.Read);
        var length = (int)vs.Length;
        var bytes = System.Buffers.ArrayPool<byte>.Shared.Rent(length);
        try {
            await vs.ReadExactlyAsync(bytes, 0, length, ct).ConfigureAwait(false);
            var hash = ComputeContentHash(bytes.AsSpan(0, length));
            var content = System.Text.Encoding.UTF8.GetString(bytes, 0, length);
            return (content, hash);
        } finally {
            System.Buffers.ArrayPool<byte>.Shared.Return(bytes);
        }
    }
}