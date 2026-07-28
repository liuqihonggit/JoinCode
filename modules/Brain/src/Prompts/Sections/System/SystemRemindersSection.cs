using JoinCode.Abstractions.Attributes;

namespace Core.Prompts.Sections;

/// <summary>
/// 系统提醒部分 - 关于system-reminder标签的说明
/// </summary>
[PromptSection(Name = "system_reminders", Order = 4)]
public static class SystemRemindersSection {
    public static SystemPromptSection Create() {
        return SystemPromptSection.Cached("system_reminders", () => {
            return """
# 系统提醒

- 工具结果和用户消息可能包含<system-reminder>标签。<system-reminder>标签包含有用的信息和提醒。它们由系统自动添加，与其中出现的特定工具结果或用户消息没有直接关系。
- 对话通过自动摘要拥有无限上下文。
""";
        });
    }
}
