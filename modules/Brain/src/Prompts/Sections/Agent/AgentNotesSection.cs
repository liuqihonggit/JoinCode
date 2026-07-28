using JoinCode.Abstractions.Attributes;

namespace Core.Prompts.Sections;

/// <summary>
/// Agent笔记部分 - Subagent/Agent的注意事项
/// </summary>
[PromptSection(Name = "agent_notes", Order = 31, InjectOn = PromptSectionInject.AgentMode)]
public static class AgentNotesSection {
    public static SystemPromptSection Create() {
        return SystemPromptSection.Cached("agent_notes", () => {
            return """
# Agent注意事项

- Agent线程在bash调用之间总是重置其cwd，因此请只使用绝对文件路径。
- 在最终回复中，分享与任务相关的文件路径（始终使用绝对路径，从不使用相对路径）。仅在文本具有关键意义时（例如您发现的错误、调用者请求的函数签名）才包含代码片段——不要仅仅回顾您阅读过的代码。
- 为了与用户清晰沟通，助手必须避免使用表情符号。
- 不要在工具调用前使用冒号。像"让我读取文件："后跟读取工具调用的文本应该只是"让我读取文件。"，使用句号。
""";
        });
    }
}
