namespace JoinCode.Abstractions.Models.Agent;

/// <summary>
/// 调用点 — 某符号在代码库中被引用的位置
/// </summary>
public sealed record CodeCallSite
{
    public required string FilePath { get; init; }
    public required int LineNumber { get; init; }
    public required string LineContent { get; init; }
    public required string MatchType { get; init; }
}
