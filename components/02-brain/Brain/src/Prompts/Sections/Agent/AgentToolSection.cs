using JoinCode.Abstractions.Attributes;
using Core.Prompts;

namespace Core.Prompts.Sections;

/// <summary>
/// Agent工具部分 - 如何使用Agent/Subagent
/// 对齐 TS shouldInjectAgentListInMessages = true 模式：
/// Agent 列表通过 system-reminder 附件注入，系统提示词只引用
/// </summary>
[PromptSection(Name = "agent_tool", Order = 12)]
public static class AgentToolSection {
    public static string? GetContent() {
        var isCoordinator = PromptConfigSnapshot.Current.IsCoordinatorMode;
        if (isCoordinator)
        {
            return "使用 Agent 工具生成工作者执行任务。工作者自主完成研究、实现和验证。可用代理类型列在对话中的 <system-reminder> 消息中。";
        }

        return """
# 使用Agent工具

使用Agent工具与专门的Agent配合，当手头的任务与Agent的描述匹配时。
Subagent对于并行化独立查询或保护主上下文窗口免受过多结果的影响很有价值，但在不需要时不应过度使用。
重要的是，避免重复Subagent已经在做的工作——如果您将研究委托给Subagent，请不要自己也执行相同的搜索。

可用代理类型列在对话中的 <system-reminder> 消息中。
""";
    }

    public static SystemPromptSection Create() =>
        SystemPromptSection.Dynamic("agent_tool", GetContent);

    /// <summary>
    /// 获取工具描述 — 供 ToolListingService 复用
    /// </summary>
    internal static string GetToolsDescription(JoinCode.Abstractions.Prompts.ToolPrompts.AgentDefinition agent)
    {
        if (agent.Tools is { Count: > 0 } tools && agent.DisallowedTools is { Count: > 0 } disallowedTools)
        {
            var denySet = new HashSet<string>(disallowedTools);
            var effectiveTools = tools.Where(t => !denySet.Contains(t)).ToList();
            return effectiveTools.Count == 0 ? "无" : string.Join(", ", effectiveTools);
        }

        if (agent.Tools is { Count: > 0 } toolsOnly) return string.Join(", ", toolsOnly);
        if (agent.DisallowedTools is { Count: > 0 } disallowedOnly) return $"除 {string.Join(", ", disallowedOnly)} 外的所有工具";

        return "所有工具";
    }
}
