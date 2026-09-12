namespace JoinCode.Abstractions.CodeIndex;

/// <summary>
/// 枢纽节点信息 — 按连接度排序的关键符号
/// </summary>
public sealed record HubNodeInfo
{
    public required string SymbolName { get; init; }
    public required int InDegree { get; init; }
    public required int OutDegree { get; init; }
    public required int TotalDegree { get; init; }
    public string? FilePath { get; init; }
}
