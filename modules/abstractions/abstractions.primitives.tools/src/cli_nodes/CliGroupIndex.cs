namespace JoinCode.Abstractions.Tools;

/// <summary>
/// 首轮提示词里的一个工具分组 —— 一个独立 exe 的定位信息。
/// <para>存在理由：模型在首轮只看得到 <c>base.yaml</c> 的 4 个工具，其余能力的入口是
/// 「某个 exe 的 <c>-h</c>」。不告诉它有哪些 exe，它只能靠猜命令名，猜不中就改用裸 shell 硬拼。</para>
/// </summary>
public sealed record CliGroupEntry {
    /// <summary>分组 ID，与 <c>node.yaml</c> / <c>jcc tool call</c> 的组名一致。</summary>
    public required string Id { get; init; }

    /// <summary>可执行文件名（不含 <c>.exe</c>），如 <c>jcc-tool-file</c>。</summary>
    public required string Exe { get; init; }

    /// <summary>一句话定位，用于提示词里区分「该找哪个组」。</summary>
    public required string Blurb { get; init; }

    /// <summary>该组工具总数。</summary>
    public int ToolCount { get; init; }

    /// <summary>其中在本 exe 内实现的工具数。</summary>
    public int LocalCount { get; init; }

    /// <summary>其中委托另一个独立 exe 实现的工具数。</summary>
    public int DelegateCount { get; init; }

    /// <summary>不经过宿主的工具数 = local + delegate。</summary>
    public int IsolatedCount => LocalCount + DelegateCount;
}

/// <summary>
/// 工具分组索引 — 首轮提示词「有哪些独立 exe」的唯一数据源。
/// <para>来源为 <c>resources/tools/cli_groups.yaml</c> 内嵌资源，由
/// <c>scripts/migrate_mcp_tools_to_cli_nodes.py</c> 从各分组 <c>node.yaml</c> 生成。
/// 禁止在提示词里手写组名清单：加一个组而提示词不知道，模型就永远发现不了它。</para>
/// </summary>
public sealed class CliGroupIndex {
    private CliGroupIndex(IReadOnlyList<CliGroupEntry> groups) {
        var duplicate = groups.GroupBy(group => group.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException($"cli_groups.yaml 存在重复分组 id '{duplicate.Key}'");

        Groups = groups;
    }

    /// <summary>全部分组（按索引文件顺序，稳定可测）。</summary>
    public IReadOnlyList<CliGroupEntry> Groups { get; }

    /// <summary>分组数。</summary>
    public int GroupCount => Groups.Count;

    /// <summary>全部工具数。</summary>
    public int ToolCount => Groups.Sum(group => group.ToolCount);

    /// <summary>不经过宿主的工具数（local + delegate）。</summary>
    public int IsolatedToolCount => Groups.Sum(group => group.IsolatedCount);

    /// <summary>从内嵌资源加载。</summary>
    public static CliGroupIndex LoadDefault() {
        var assembly = typeof(CliGroupIndex).Assembly;
        const string resourceName = "JoinCode.CliNodes.cli_groups.yaml";
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"未找到内嵌资源 '{resourceName}'，请检查 csproj 的 EmbeddedResource 声明");
        using var reader = new StreamReader(stream);
        return FromText(reader.ReadToEnd(), resourceName);
    }

    /// <summary>从 YAML 文本加载（测试与生成期对账用）。</summary>
    public static CliGroupIndex FromText(string yaml, string source = "<text>") =>
        new(NodeYamlParser.ParseGroupIndex(yaml, source));
}
