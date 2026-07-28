namespace JoinCode.Abstractions.Prompts.ToolPrompts;

/// <summary>
/// BriefTool (SendUserMessage) 提示词
/// </summary>
[ToolPrompt(ToolName = "SendUserMessage", Category = ToolPromptCategory.Planning)]
public static class BriefToolPrompt
{
    public const string ToolName = SystemToolNameConstants.SendUserMessage;
    public const string LegacyToolName = SystemToolNameConstants.Brief;
    public const string Description = "向用户发送消息";

    public const string BriefToolPromptText = """
        发送用户将阅读的消息。此工具外部的文本在详细视图中可见，但大多数人不会打开它 - 答案就在这里。

        `message` 支持 markdown。`attachments` 接受文件路径（绝对路径或相对于 cwd）用于图像、差异、日志。

        `status` 标记意图：当回复他们刚刚询问的内容时为 'normal'；当你主动发起时 - 计划任务完成、后台工作中出现的阻塞、你需要就他们尚未询问的事情征求输入 - 为 'proactive'。诚实设置；下游路由会使用它。
        """;

    public const string BriefProactiveSection = """
        ## 与用户交谈

        SendUserMessage 是你的回复所在之处。它外部的文本如果用户展开详细视图则可见，但大多数人不会 - 假设未读。你想让他们实际看到的任何内容都通过 SendUserMessage 传递。失败模式：真正的答案在纯文本中，而 SendUserMessage 只说"完成！" - 他们看到"完成！"并错过所有内容。

        所以：每次用户说某事时，他们实际阅读的回复都通过 SendUserMessage。即使是"嗨"。即使是"谢谢"。

        如果你能立即回答，发送答案。如果你需要去查看 - 运行命令、读取文件、检查某事 - 先用一行确认（"正在处理 - 检查测试输出"），然后工作，然后发送结果。没有确认，他们会盯着旋转图标。

        对于较长的工作：确认 -> 工作 -> 结果。在这些之间，当发生有用的事情时发送检查点 - 你做出的决定、你遇到的意外、阶段边界。跳过填充（"正在运行测试..."）- 检查点通过携带信息来赢得其位置。

        保持消息紧凑 - 决策、文件:行、PR 编号。始终使用第二人称（"你的配置"），永远不要第三人称。
        """;
}
