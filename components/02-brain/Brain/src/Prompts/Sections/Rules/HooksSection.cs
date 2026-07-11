using JoinCode.Abstractions.Attributes;

namespace Core.Prompts.Sections;

/// <summary>
/// Hooks部分 - 关于用户配置的hooks
/// </summary>
[PromptSection(Name = "hooks", Order = 5)]
public static class HooksSection {
    public static SystemPromptSection Create() {
        return SystemPromptSection.Cached("hooks", () => {
            return """
# Hooks

用户可以在设置中配置'hooks'，即在响应事件（如工具调用）时执行的shell命令。
将来自hooks的反馈（包括<user-prompt-submit-hook>）视为来自用户。
如果您被hook阻止，确定您是否可以根据被阻止的消息调整您的操作。
如果不能，请用户检查他们的hooks配置。
""";
        });
    }
}
