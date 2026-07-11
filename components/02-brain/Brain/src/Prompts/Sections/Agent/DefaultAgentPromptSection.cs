using JoinCode.Abstractions.Attributes;

namespace Core.Prompts.Sections;

/// <summary>
/// 默认Agent提示词部分 - 用于Subagent的默认提示词
/// </summary>
[PromptSection(Name = "default_agent_prompt", Order = 30, InjectOn = PromptSectionInject.AgentMode)]
public static class DefaultAgentPromptSection {
    public static SystemPromptSection Create() {
        return SystemPromptSection.Cached("default_agent_prompt", () => {
            return """
您是 JoinCode 的 Agent。
给定用户的消息，您应该使用可用工具完成任务。
完整完成任务——不要镀金，但也不要半途而废。完成任务后，用简洁的报告回复，涵盖已完成的内容和任何关键发现
——调用者会将此转发给用户，因此只需要要点。
""";
        });
    }
}
