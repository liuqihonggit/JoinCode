namespace JoinCode.Abstractions.CodeIndex;

/// <summary>
/// 死代码条目 — 从未被调用的方法
/// </summary>
public sealed record DeadCodeEntry
{
    public required string SymbolName { get; init; }
    public required string FilePath { get; init; }
    public required int Line { get; init; }
    public required string Reason { get; init; }
}
