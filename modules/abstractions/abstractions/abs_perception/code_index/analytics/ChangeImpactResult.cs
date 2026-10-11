namespace JoinCode.Abstractions.CodeIndex;

/// <summary>
/// 变更影响分析结果 — 文件变更对全局的影响范围
/// </summary>
public sealed record ChangeImpactResult {
    /// <summary>获取已变更文件列表。</summary>
    public required IReadOnlyList<string> ChangedFiles { get; init; }
    /// <summary>获取受影响符号列表。</summary>
    public required IReadOnlyList<string> AffectedSymbols { get; init; }
    /// <summary>获取受影响文件列表。</summary>
    public required IReadOnlyList<string> AffectedFiles { get; init; }
    /// <summary>获取受影响项目列表。</summary>
    public required IReadOnlyList<string> AffectedProjects { get; init; }
}