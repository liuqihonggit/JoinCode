namespace JoinCode.Abstractions.CodeIndex;

/// <summary>
/// 变更影响分析结果 — 文件变更对全局的影响范围
/// </summary>
public sealed record ChangeImpactResult
{
    public required IReadOnlyList<string> ChangedFiles { get; init; }
    public required IReadOnlyList<string> AffectedSymbols { get; init; }
    public required IReadOnlyList<string> AffectedFiles { get; init; }
    public required IReadOnlyList<string> AffectedProjects { get; init; }
}
