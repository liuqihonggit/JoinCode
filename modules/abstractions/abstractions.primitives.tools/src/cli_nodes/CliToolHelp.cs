using System.Text;

namespace JoinCode.Abstractions.Tools;

/// <summary>
/// 把节点自描述渲染成 -h 帮助文本。
/// 这是 LLM 按需阅读工具说明的唯一入口 — 描述永不进入系统提示词。
/// </summary>
public static class CliToolHelp {
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
            sb.AppendLine($"进程隔离覆盖: {descriptor.LocalToolCount}/{descriptor.Tools.Length}（local=本 exe 内实现，delegate=委托独立 exe，其余转发宿主）");
    }

    /// <summary>
    /// 列出分组内的子工具及执行方式 — 让「哪些工具真跑在独立进程里」成为可读事实，
    /// 而不是交付说明里的一句口头承诺。
    /// </summary>
    private static void AppendTools(StringBuilder sb, CliToolDescriptor descriptor) {
        if (!descriptor.HasToolEntries) return;
        sb.AppendLine();
        sb.AppendLine("子工具（local=本 exe 内实现，delegate=委托独立 exe，host=转发宿主执行）:");
        foreach (var entry in descriptor.Tools.OrderBy(item => item.Id, StringComparer.Ordinal)) {
            var aliases = entry.Aliases.Length == 0 ? string.Empty : $"  别名: {string.Join(", ", entry.Aliases)}";
            sb.AppendLine($"  {entry.Id,-32} {entry.ModeLabel}{aliases}");
        }
    }

    private static void AppendUsage(StringBuilder sb, CliToolDescriptor descriptor) {
        sb.AppendLine();
        sb.AppendLine("用法:");
        var required = descriptor.Parameters
            .Where(parameter => parameter.Required)
            .Select(parameter => $"<{parameter.Name}>")
            .ToArray();
        sb.AppendLine($"  {descriptor.Exe ?? descriptor.Id} {string.Join(" ", required)} [options]".TrimEnd());
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
