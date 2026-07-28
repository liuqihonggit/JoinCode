namespace JoinCode.Abstractions.CodeIndex;

/// <summary>
/// 综合检索结果 — 模糊匹配符号 + 全部引用 + 调用方/被调用方,受 token 预算限制
/// </summary>
public sealed record ComprehensiveSearchResult
{
    /// <summary>模糊匹配到的符号列表(最多 100 个,超过时仅返回前 100 个)</summary>
    public required IReadOnlyList<SymbolInfo> MatchedSymbols { get; init; }

    /// <summary>匹配符号的实际匹配总数(可能大于 MatchedSymbols.Count,超过部分被候选上限截断)</summary>
    public required int TotalMatchedCount { get; init; }

    /// <summary>匹配符号的全部引用位置(调用点,FilePath/StartLine = 调用位置)</summary>
    public required IReadOnlyList<SymbolInfo> References { get; init; }

    /// <summary>调用匹配符号的边(谁调用了它)</summary>
    public required IReadOnlyList<CallEdge> Callers { get; init; }

    /// <summary>匹配符号调用的边(它调用了谁)</summary>
    public required IReadOnlyList<CallEdge> Callees { get; init; }

    /// <summary>估算的 token 总数(约 4 字符/token)</summary>
    public required int EstimatedTokens { get; init; }

    /// <summary>是否因 token 预算被截断</summary>
    public required bool Truncated { get; init; }

    /// <summary>因 token 预算被截断的条目数(所有类别的截断总和)</summary>
    public required int TruncatedCount { get; init; }

    /// <summary>检索耗时(毫秒)</summary>
    public required long ElapsedMs { get; init; }
}
