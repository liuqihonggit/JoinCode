namespace JoinCode.Abstractions.Prompts.ToolPrompts;

/// <summary>
/// SleepTool 提示词
/// </summary>
[ToolPrompt(ToolName = "Sleep", Category = ToolPromptCategory.Shell)]
public static class SleepToolPrompt
{
    public const string ToolName = SystemToolNameConstants.Sleep;
    public const string Description = "等待指定持续时间";

    public const string SleepToolPromptText = """
        等待指定持续时间。用户可以随时中断睡眠。

        当用户告诉你睡觉或休息、当你无事可做、或当你在等待某事时使用此工具。

        你可能会收到 <tick> 提示 —— 这些是定期检查。在睡觉之前寻找有用的工作做。

        你可以与其他工具并发调用此工具 —— 它不会干扰它们。

        优先使用此工具而不是 `Bash(sleep ...)` —— 它不会占用 shell 进程。

        每次唤醒花费一次 API 调用，但提示词缓存在 5 分钟不活动后过期 —— 相应平衡。
        """;
}
