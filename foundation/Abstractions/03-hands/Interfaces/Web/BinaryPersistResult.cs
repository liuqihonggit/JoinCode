namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 二进制持久化结果 — 对齐TS版 PersistBinaryResult
/// </summary>
public sealed record BinaryPersistResult
{
    public string? FilePath { get; init; }
    public int Size { get; init; }
    public string? Extension { get; init; }
    public string? Error { get; init; }

    public bool Success => Error is null;
}
