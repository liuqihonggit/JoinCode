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

    /// <summary>
    /// 父节点 ID — "省略即继承"：子只声明差异，未提及的字段从父节点继承。
    /// null 表示不继承（绝大多数节点）。解析由 <see cref="CliNodeInheritanceResolver"/> 完成。
    /// </summary>
    public string? Inherits { get; init; }

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

    /// <summary>按工具 ID、别名、全名/短名（<c>file_read</c> ↔ <c>read</c>）或组内同义词查找子工具条目。</summary>
    /// <param name="toolId">工具 ID 或任意等价写法（大小写不敏感）。</param>
    /// <returns>命中条目；未声明或名称有歧义时返回 <see langword="null"/>。</returns>
    public CliToolEntry? FindTool(string toolId) {
        // 条目自带的 ID / aliases 优先 —— 斜杠组的 aliases 与 ID 完全等价，不参与前缀推导
        var direct = Tools.FirstOrDefault(entry => entry.Matches(toolId));
        if (direct is not null) return direct;

        // 再走全名/短名/同义词互认，规则与分组 exe 侧共用 ToolAliasTable（唯一数据源）
        var resolved = ToolAliasTable.Resolve(Id, Tools.Select(entry => entry.Id).ToArray(), toolId);
        return resolved is null
            ? null
            : Tools.FirstOrDefault(entry => string.Equals(entry.Id, resolved, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>是否为进入协议原生 tools 字段的基础工具。</summary>
    public bool IsBaseTool => Exe is null;

    /// <summary>
    /// 解析时 raw YAML 中显式出现过的键名 — 仅供继承合并判断"省略 vs 显式声明"。
    /// 用 internal 隔离在解析层，不进公开 DTO 语义；手工 new 的 descriptor 为空，
    /// 表示"全部字段显式"，此时继承不会覆盖任何字段（符合直觉）。
    /// </summary>
    internal IReadOnlySet<string> DeclaredKeys { get; init; } = new HashSet<string>(StringComparer.Ordinal);
}
