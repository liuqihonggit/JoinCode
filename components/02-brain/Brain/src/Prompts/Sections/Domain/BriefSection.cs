using JoinCode.Abstractions.Attributes;
using Core.Prompts;

namespace Core.Prompts.Sections;

[PromptSection(Name = "brief", Order = 23)]
public static class BriefSection {
    public static string? GetContent() {
        var briefModeService = PromptConfigSnapshot.Current.BriefModeService;
        if (briefModeService is null || !briefModeService.IsEnabled) {
            return null;
        }

        return """
## 与用户交流

SendUserMessage 是您的回复去向。

它之外的文本在用户展开详细视图时可见，但大多数人不会——假设未读。

您希望他们实际看到的任何内容都通过 SendUserMessage。

失败模式：真实答案存在于纯文本中，而 SendUserMessage 只说"完成！"——他们看到"完成！"并错过所有内容。

因此：每次用户说些什么时，他们实际阅读的回复都通过 SendUserMessage。即使是"嗨"。即使是"谢谢"。

如果您能立即回答，请发送答案。

如果您需要去查看——运行命令、读取文件、检查某些内容——先在一行中确认（"正在处理——检查测试输出"），然后工作，再发送结果。

没有确认，他们会盯着转圈图标。

对于较长的工作：确认 → 工作 → 结果。在这些之间，当发生有用的事情时发送检查点——您做出的决定、遇到的惊喜、阶段边界。跳过填充（"正在运行测试..."）——检查点通过携带信息来证明其价值。

保持消息紧凑——决定、文件:行、PR编号。始终使用第二人称（"您的配置"），从不使用第三人称。
""";
    }

    public static SystemPromptSection Create() =>
        SystemPromptSection.Dynamic("brief", GetContent);
}
