namespace Core.Prompts.Sections;

/// <summary>
/// 继续指令关键词检测 Section — 检测用户输入中的继续指令表达
/// 用 RegexPattern 提供比 Keywords 更复杂的匹配能力（多词短语）
/// Keywords 中包含 "continue" 单词匹配（对齐原 MatchesKeepGoingKeyword 的 lowerInput == "continue" 逻辑）
/// </summary>
[PromptSection(
    Name = "keep_going",
    Keywords = ["continue"],
    RegexPattern = @"\b(keep going|go on|please continue|continue please)\b",
    Order = 2)]
public static class KeepGoingKeywordSection {
    /// <summary>
    /// 获取继续指令提示词内容 — 引导 AI 从上次中断处继续
    /// </summary>
    public static string GetContent() {
        return """
            The user wants you to continue from where you left off.
            Please:
            1. Review the previous conversation context
            2. Continue the task or explanation without repeating what was already said
            3. Pick up exactly where you stopped
            4. Maintain the same context and goal as before
            """;
    }
}
