namespace JoinCode.Abstractions.CodeIndex;

/// <summary>
/// 死代码条目 — 从未被调用的方法
/// </summary>
public sealed record DeadCodeEntry {
    /// <summary>获取符号名称。</summary>
    public required string SymbolName { get; init; }
    /// <summary>获取文件路径。</summary>
    public required string FilePath { get; init; }
    /// <summary>获取行号。</summary>
    public required int Line { get; init; }
    /// <summary>获取判定为死代码的原因。</summary>
    public required string Reason { get; init; }
}