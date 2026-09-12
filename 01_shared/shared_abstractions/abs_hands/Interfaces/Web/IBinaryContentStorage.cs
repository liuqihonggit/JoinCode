namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 二进制内容持久化接口 — 对齐TS版 mcpOutputStorage.ts 的 persistBinaryContent
/// </summary>
public interface IBinaryContentStorage
{
    /// <summary>
    /// 持久化二进制内容到文件
    /// </summary>
    /// <param name="bytes">原始字节</param>
    /// <param name="mimeType">MIME类型</param>
    /// <param name="persistId">持久化ID，格式: webfetch-{timestamp}-{random}</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>持久化结果（路径+大小+扩展名，或错误信息）</returns>
    Task<BinaryPersistResult> PersistAsync(
        byte[] bytes,
        string? mimeType,
        string persistId,
        CancellationToken cancellationToken = default);

    string GeneratePersistId();
}
