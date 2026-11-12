namespace Core.Prompts.Sections;

/// <summary>
/// 负面情绪关键词检测 Section — 检测用户输入中的负面情绪表达
/// 用 RegexPattern 提供比 Keywords 更复杂的匹配能力（多词短语、变体拼写）
/// </summary>
[PromptSection(
    Name = "negative",
    Keywords = [],
    RegexPattern = @"\b(wtf|wth|ffs|omfg|shit(ty|tiest)?|dumbass|horrible|awful|piss(ed|ing)? off|piece of (shit|crap|junk)|what the (fuck|hell)|fucking? (broken|useless|terrible|awful|horrible)|fuck you|screw (this|you)|so frustrating|this sucks|damn it)\b",
    Order = 1)]
public static class NegativeKeywordSection {
    /// <summary>
    /// 获取负面情绪提示词内容 — 引导 AI 以同理心回应用户的不满
    /// </summary>
    public static string GetContent() {
        return """
            The user seems frustrated or dissatisfied with the previous response.
            Please:
            1. Acknowledge their frustration with empathy
            2. Apologize for any shortcomings
            3. Offer to help in a different way or clarify your previous response
            4. Be more careful and thorough in your next response
            """;
    }
}
