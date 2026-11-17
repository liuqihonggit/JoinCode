namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 参数名修复器 — 转发到契约层的唯一实现。
/// <para>实现已下沉到 <c>JoinCode.Abstractions.Tools.ToolParameterNameRepairer</c>：
/// 独立工具 exe 受依赖墙约束只能引用契约层，规则留在 utils 就必然出现第二套实现。</para>
/// </summary>
internal static class ParameterNameRepairer {
    /// <summary>
    /// 修复参数名 — 将 LLM 发送的别名/大小写不匹配的参数名映射为 schema 中定义的标准名。
    /// </summary>
    public static (Dictionary<string, JsonElement> Arguments, bool Modified, string? Hint) RepairParameterNames(
        Dictionary<string, JsonElement> arguments,
        ToolSchema schema) =>
        ToolParameterNameRepairer.RepairParameterNames(arguments, schema);
}
