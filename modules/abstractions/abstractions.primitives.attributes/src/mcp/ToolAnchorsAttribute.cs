namespace JoinCode.Abstractions.Attributes;

/// <summary>
/// 工具锚点特性 — 标注工具的 3~5 个锚点关键词，用于向量检索匹配用户问题
/// 锚点是用户问题描述中的关键词，命中时注入对应工具 schema 到上下文
/// </summary>
/// <remarks>
/// 用法: [McpTool("gh_run_view", ...)] [ToolAnchors("CI 失败", "job 日志", "run 状态", "workflow 排错")]
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class ToolAnchorsAttribute : Attribute {
    /// <summary>获取锚点关键词集合。</summary>
    public string[] Anchors { get; }

    /// <summary>
    /// 构造工具锚点特性 — 传入 3~5 个锚点关键词
    /// </summary>
    /// <param name="anchors">锚点关键词集合</param>
    public ToolAnchorsAttribute(params string[] anchors) {
        Anchors = anchors ?? [];
    }
}
