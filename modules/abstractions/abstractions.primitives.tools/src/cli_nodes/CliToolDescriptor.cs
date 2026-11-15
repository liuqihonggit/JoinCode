namespace JoinCode.Abstractions.Tools;

/// <summary>
/// 一个 CLI 节点的完整自描述 — node.yaml（独立工具）或 base.yaml 条目（基础工具）。
/// 万物皆节点：基础工具与独立 CLI.exe 共用同一描述结构，差别只在 Exe 是否为空。
/// </summary>
public sealed record CliToolDescriptor {
    /// <summary>节点唯一 ID（如 rg / file_read），也是 CLI 调用名。</summary>
    public required string Id { get; init; }

    /// <summary>显示名。</summary>
    public required string Name { get; init; }

    /// <summary>一句话说明 — 搜索下钻时的主要匹配文本。</summary>
    public required string Summary { get; init; }

    /// <summary>自然分类（用于目录树，不用于 MCP 分组）。</summary>
    public string Category { get; init; } = "general";

    /// <summary>独立 exe 文件名；基础工具为 null（宿主内实现）。</summary>
    public string? Exe { get; init; }

    /// <summary>搜索关键词。</summary>
    public string[] Tags { get; init; } = [];

    /// <summary>调用示例，-h 输出。</summary>
    public string[] Examples { get; init; } = [];

    /// <summary>参数定义。</summary>
    public CliToolParameter[] Parameters { get; init; } = [];

    /// <summary>
    /// 分组节点内的子工具清单（含执行方式）；基础工具与未声明时为空。
    /// 供「按工具名反查分组」与「进程隔离覆盖率审计」使用。
    /// </summary>
    public CliToolEntry[] Tools { get; init; } = [];

    /// <summary>本地实现（真进程隔离）的子工具数量。</summary>
    public int LocalToolCount => Tools.Count(entry => entry.IsLocal);

    /// <summary>是否有子工具清单（分组节点为 true）。</summary>
    public bool HasToolEntries => Tools.Length > 0;

    /// <summary>按工具 ID 或别名查找子工具条目。</summary>
    /// <param name="toolId">工具 ID 或别名（大小写不敏感）。</param>
    /// <returns>命中条目；未声明时返回 <see langword="null"/>。</returns>
    public CliToolEntry? FindTool(string toolId) =>
        Tools.FirstOrDefault(entry => entry.Matches(toolId));

    /// <summary>是否为进入协议原生 tools 字段的基础工具。</summary>
    public bool IsBaseTool => Exe is null;
}
