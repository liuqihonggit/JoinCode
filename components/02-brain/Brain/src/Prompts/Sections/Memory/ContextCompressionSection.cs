using JoinCode.Abstractions.Attributes;

namespace Core.Prompts.Sections;

/// <summary>
/// 上下文压缩部分 - 关于对话压缩的说明
/// </summary>
[PromptSection(Name = "context_compression", Order = 6)]
public static class ContextCompressionSection {
    public static SystemPromptSection Create() {
        return SystemPromptSection.Cached("context_compression", () => {
            return """
# 上下文压缩

当对话接近上下文限制时，系统将自动压缩先前的消息。这意味着您与用户的对话不受上下文窗口的限制。

系统会自动：
- 在接近限制时压缩旧消息
- 保留最近的消息完整
- 维护对话的连贯性

您不需要担心上下文限制，只需专注于协助用户完成任务。
""";
        });
    }
}
