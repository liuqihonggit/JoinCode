namespace Api.LLM;

/// <summary>
/// 工具发现排序 — 两阶段加载下 <see cref="SystemToolName.ToolSearch"/> 是全部组名/deferred 工具的
/// 唯一发现入口（组别展开 list_groups、分组下钻 map[]、关键词搜索均走它），必须置于请求工具数组
/// **首位**让模型第一眼看到（TASK078 用户指令：搜索工具与组别展开必须 System 注入且排首位）。
/// </summary>
internal static class ToolDiscoveryOrdering {
    /// <summary>把 tool_search 稳定移到工具数组首位（其余相对顺序不变；不含 tool_search 时原样返回）。</summary>
    /// <typeparam name="T">工具定义类型（AnthropicToolDefinition / OpenAITool / ResponsesTool）。</typeparam>
    /// <param name="tools">请求工具数组。</param>
    /// <param name="nameSelector">工具名取值器。</param>
    public static List<T> PutToolSearchFirst<T>(List<T> tools, Func<T, string?> nameSelector) {
        var index = tools.FindIndex(t => string.Equals(nameSelector(t), SystemToolName.ToolSearch.ToValue(), StringComparison.Ordinal));
        if (index <= 0) {
            return tools;
        }

        var ordered = new List<T>(tools.Count) { tools[index] };
        ordered.AddRange(tools.Take(index));
        ordered.AddRange(tools.Skip(index + 1));
        return ordered;
    }
}
