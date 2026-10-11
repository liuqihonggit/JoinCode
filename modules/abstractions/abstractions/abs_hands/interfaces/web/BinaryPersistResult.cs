namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 二进制持久化结果 — 对齐TS版 PersistBinaryResult
/// </summary>
public sealed record BinaryPersistResult {
    /// <summary>获取文件路径。</summary>
    public string? FilePath { get; init; }
    /// <summary>获取文件大小（字节）。</summary>
    public int Size { get; init; }
    /// <summary>获取文件扩展名。</summary>
    public string? Extension { get; init; }
    /// <summary>获取错误信息。</summary>
    public string? Error { get; init; }

    /// <summary>获取一个值，指示持久化是否成功。</summary>
    public bool Success => Error is null;
}