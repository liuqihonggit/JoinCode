namespace JoinCode.Abstractions.Tools;

/// <summary>
/// 超边配置 — 定义一组语义关联的工具共享评分空间
/// </summary>
public record HyperedgeSettings {
    /// <summary>
    /// 超边标识
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// 成员工具名称列表
    /// </summary>
    public List<string> ToolNames { get; init; } = [];

    /// <summary>
    /// 超边权重（0-1），影响共享评分在最终评分中的占比
    /// </summary>
    public double Weight { get; init; } = 0.5;

    /// <summary>
    /// 链路顺序 — LLM使用工具A后推荐的后续工具序列
    /// </summary>
    public List<string> ChainOrder { get; init; } = [];

    /// <summary>
    /// 转换为 ToolHyperedge
    /// </summary>
    public ToolHyperedge ToHyperedge() => new() {
        Id = Id,
        ToolNames = FrozenSet.Create(StringComparer.OrdinalIgnoreCase, [.. ToolNames]),
        Weight = Weight,
        ChainOrder = ChainOrder?.ToArray()
    };
}
