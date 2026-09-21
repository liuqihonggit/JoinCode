namespace JoinCode.Abstractions.Utils;

public sealed class ToolCallRepairResult {
    public required bool Success { get; init; }
    public required string RepairedJson { get; init; }
    public string? RepairHint { get; init; }
}

public sealed class ArgumentRepairResult {
    public required Dictionary<string, JsonElement> RepairedArguments { get; init; }
    public string? RepairHint { get; init; }
}

/// <summary>
/// 工具调用修复服务 — Facade,委托给各单一职责小类
/// <para>JsonRepairPipeline: JSON 字符串多阶段修复</para>
/// <para>ParameterNameRepairer: 参数名别名/大小写修复</para>
/// <para>ArgumentTypeCoercer: 参数类型强转</para>
/// <para>ToolNameResolver: 工具名归一化/推荐</para>
/// <para>ShellCallExampleBuilder: Shell 调用示例生成</para>
/// </summary>
internal static class ToolCallRepairService {
    /// <summary>
    /// 修复 LLM 生成的非法 JSON — 委托给 JsonRepairPipeline
    /// </summary>
    public static ToolCallRepairResult RepairJson(string? rawJson)
        => JsonRepairPipeline.RepairJson(rawJson);

    /// <summary>
    /// 修复工具调用参数 — 编排参数名修复 + 参数类型强转
    /// </summary>
    public static ArgumentRepairResult RepairArguments(
        string toolName,
        Dictionary<string, JsonElement> arguments,
        ToolSchema? schema) {
        if (arguments is null || arguments.Count == 0)
            return new ArgumentRepairResult { RepairedArguments = arguments ?? new Dictionary<string, JsonElement>() };

        if (schema?.Properties is null || schema.Properties.Count == 0)
            return new ArgumentRepairResult { RepairedArguments = arguments };

        var repaired = arguments;
        var hints = new List<string>();
        var modified = false;

        var nameRepairs = ParameterNameRepairer.RepairParameterNames(arguments, schema);
        if (nameRepairs.Modified) {
            repaired = nameRepairs.Arguments;
            hints.Add(nameRepairs.Hint ?? "Parameter names repaired");
            modified = true;
        }

        var typeRepairs = ArgumentTypeCoercer.RepairArgumentTypes(repaired, schema);
        if (typeRepairs.Modified) {
            repaired = typeRepairs.Arguments;
            hints.Add(typeRepairs.Hint ?? "Argument types repaired");
            modified = true;
        }

        return new ArgumentRepairResult {
            RepairedArguments = modified ? repaired : arguments,
            RepairHint = hints.Count > 0 ? string.Join("; ", hints) : null
        };
    }

    /// <summary>
    /// 工具名归一化 — 委托给 ToolNameResolver
    /// </summary>
    public static string RepairToolName(string? toolName)
        => ToolNameResolver.RepairToolName(toolName);

    /// <summary>
    /// 工具名模糊匹配 — 委托给 ToolNameResolver
    /// </summary>
    public static IReadOnlyList<string> SuggestToolNames(string input, IEnumerable<string> availableTools)
        => ToolNameResolver.SuggestToolNames(input, availableTools);

    /// <summary>
    /// 生成跨 shell 调用示例文本 — 委托给 ShellCallExampleBuilder
    /// </summary>
    internal static string BuildShellCallExamples(string toolName, ToolSchema? schema = null)
        => ShellCallExampleBuilder.BuildShellCallExamples(toolName, schema);

    /// <summary>
    /// 检测"引号被 shell 剥落"特征并返回修正写法提示 — 委托给 ShellCallExampleBuilder
    /// </summary>
    internal static string? BuildShellQuoteHint(string json)
        => ShellCallExampleBuilder.BuildShellQuoteHint(json);
}