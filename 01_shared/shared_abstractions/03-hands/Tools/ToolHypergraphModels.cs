namespace JoinCode.Abstractions.Tools;

/// <summary>
/// 超边热重载接口 — 支持运行时动态更新超图超边配置
/// </summary>
public interface IHyperedgeReloadable
{
    /// <summary>
    /// 从配置加载自定义超边 — 合并预设超边和用户自定义超边
    /// </summary>
    void LoadCustomHyperedges(List<HyperedgeSettings> customHyperedges);
}

/// <summary>
/// 工具链超边 — 一组语义关联的工具共享评分空间
/// 一条超边可包含任意数量的节点（工具），不限于两个
/// </summary>
public sealed record ToolHyperedge
{
    public required string Id { get; init; }
    public required FrozenSet<string> ToolNames { get; init; }
    public int SharedScore { get; set; }
    public double Weight { get; init; } = 0.5;
    public string[]? ChainOrder { get; init; }
}

/// <summary>
/// 工具链有向超图 — 建模工具间依赖/关联关系
/// </summary>
public sealed class ToolHypergraph
{
    public List<ToolHyperedge> Hyperedges { get; init; } = new();
    public FrozenDictionary<string, List<ToolHyperedge>> ToolToEdges { get; init; } = FrozenDictionary<string, List<ToolHyperedge>>.Empty;
}
