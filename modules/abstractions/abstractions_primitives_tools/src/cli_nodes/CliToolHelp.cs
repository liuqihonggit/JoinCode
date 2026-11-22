

namespace JoinCode.Abstractions.Tools;

/// <summary>
/// 把节点自描述渲染成 -h 帮助文本。
/// 这是 LLM 按需阅读工具说明的唯一入口 — 描述永不进入系统提示词。
/// </summary>
public static class CliToolHelp {
    /// <summary>子工具清单的预览上限 —— 超出部分交给 exe 的分组索引，避免两处重复列同一份清单。</summary>
    private const int ToolListPreviewLimit = 20;

    public static string Render(CliToolDescriptor descriptor) {
        var sb = new StringBuilder();
        AppendHeader(sb, descriptor);
        AppendUsage(sb, descriptor);
        AppendTools(sb, descriptor);
        AppendParameters(sb, descriptor);
        AppendExamples(sb, descriptor);
        AppendTags(sb, descriptor);
        return sb.ToString().TrimEnd();
    }

    private static void AppendHeader(StringBuilder sb, CliToolDescriptor descriptor) {
        sb.AppendLine($"{descriptor.Name} — {descriptor.Summary}");
        sb.AppendLine();
        sb.AppendLine($"节点 ID: {descriptor.Id}");
        sb.AppendLine($"分类: {descriptor.Category}");
        if (descriptor.Exe is not null)
            sb.AppendLine($"可执行文件: {descriptor.Exe}");
        if (descriptor.HasToolEntries)
            sb.AppendLine($"独立 exe 实现: {descriptor.LocalToolCount}/{descriptor.Tools.Length}（local=本 exe 内实现，delegate=委托独立 exe，其余为 Agent 功能由 jcc 内执行）");
    }

    /// <summary>
    /// 列出分组内的子工具及执行方式 — 让「哪些工具真跑在独立进程里」成为可读事实，
    /// 而不是交付说明里的一句口头承诺。
    /// <para>大组只列前 20 个：github 的 172 行清单与分组 exe 的 <c>-h</c> 索引完全重复，
    /// 而那一份已经能按前缀折叠并按需检索。</para>
    /// </summary>
    private static void AppendTools(StringBuilder sb, CliToolDescriptor descriptor) {
        if (!descriptor.HasToolEntries) return;
        sb.AppendLine();
        sb.AppendLine("子工具（local=本 exe 内实现，delegate=委托独立 exe，host=转发宿主执行）:");
        var ordered = descriptor.Tools.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
        var shown = ordered.Length <= ToolListPreviewLimit ? ordered : ordered[..ToolListPreviewLimit];
        foreach (var entry in shown) {
            var aliases = entry.Aliases.Length == 0 ? string.Empty : $"  别名: {string.Join(", ", entry.Aliases)}";
            sb.AppendLine($"  {entry.Id,-32} {entry.ModeLabel}{aliases}");
        }
        if (ordered.Length > shown.Length) {
            sb.AppendLine($"  ...（共 {ordered.Length} 个，其余 {ordered.Length - shown.Length} 个用 `{descriptor.Exe ?? descriptor.Id} -h` 看分组索引）");
        }
    }

    private static void AppendUsage(StringBuilder sb, CliToolDescriptor descriptor) {
        sb.AppendLine();
        sb.AppendLine("用法:");
        var parts = new List<string> { descriptor.Exe ?? descriptor.Id };
        parts.AddRange(descriptor.Parameters
            .Where(parameter => parameter.Required)
            .Select(parameter => $"<{parameter.Name}>"));
        parts.Add("[options]");
        sb.AppendLine($"  {string.Join(" ", parts)}");
    }

    private static void AppendParameters(StringBuilder sb, CliToolDescriptor descriptor) {
        if (descriptor.Parameters.Length == 0) return;
        sb.AppendLine();
        sb.AppendLine("参数:");
        foreach (var parameter in descriptor.Parameters)
            sb.AppendLine($"  --{parameter.Name,-20} {parameter.Type,-10} {(parameter.Required ? "必填" : "可选"),-4} {parameter.Description ?? string.Empty}".TrimEnd());
        var withDefault = descriptor.Parameters.Where(parameter => parameter.Default is not null).ToArray();
        foreach (var parameter in withDefault)
            sb.AppendLine($"    {parameter.Name} 默认值: {parameter.Default}");
        var withAliases = descriptor.Parameters.Where(parameter => parameter.Aliases is not null).ToArray();
        foreach (var parameter in withAliases)
            sb.AppendLine($"    {parameter.Name} 别名: {parameter.Aliases}");
    }

    private static void AppendExamples(StringBuilder sb, CliToolDescriptor descriptor) {
        if (descriptor.Examples.Length == 0) return;
        sb.AppendLine();
        sb.AppendLine("示例:");
        foreach (var example in descriptor.Examples)
            sb.AppendLine($"  {example}");
    }

    private static void AppendTags(StringBuilder sb, CliToolDescriptor descriptor) {
        if (descriptor.Tags.Length == 0) return;
        sb.AppendLine();
        sb.AppendLine($"标签: {string.Join(", ", descriptor.Tags)}");
    }
}
