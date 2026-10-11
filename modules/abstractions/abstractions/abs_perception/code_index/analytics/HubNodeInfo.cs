namespace JoinCode.Abstractions.CodeIndex;

/// <summary>
/// 枢纽节点信息 — 按连接度排序的关键符号
/// </summary>
public sealed record HubNodeInfo {
    /// <summary>获取符号名称。</summary>
    public required string SymbolName { get; init; }
    /// <summary>获取入度。</summary>
    public required int InDegree { get; init; }
    /// <summary>获取出度。</summary>
    public required int OutDegree { get; init; }
    /// <summary>获取总连接度。</summary>
    public required int TotalDegree { get; init; }
    /// <summary>获取文件路径。</summary>
    public string? FilePath { get; init; }
}