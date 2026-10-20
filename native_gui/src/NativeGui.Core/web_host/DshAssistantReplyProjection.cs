namespace JoinCode.Gui.WebHost;

/// <summary>
/// 轮次事件 → DSH 助手消息投影（TASK058 B4）：引擎流事件的收窄翻译单点。
/// Thinking → content 前置 reasoning 块（前端 toAssistantBlock type=reasoning 渲染思考折叠区）；
/// Complete.Usage（<see cref="TokenUsage"/>）→ <see cref="DshTurnUsage"/> 四桶（前端 TurnUsagePanel 消费，
/// totalTokens = 四桶之和对齐前端 tokenTotal 语义）。
/// </summary>
internal static class DshAssistantReplyProjection {
    /// <summary>把一轮的思考/正文/用量投影为前端消息块序列与用量</summary>
    /// <param name="thinking">思考内容（null 或空表示无思考）</param>
    /// <param name="text">正文（可为空）</param>
    /// <param name="usage">引擎 Complete 事件的用量（null 表示未报告）</param>
    /// <returns>消息块列表与用量 DTO</returns>
    internal static (List<DshWireContentBlock> Content, DshTurnUsage? Usage) Project(string? thinking, string text, TokenUsage? usage) {
        List<DshWireContentBlock> content = [];
        if (!string.IsNullOrEmpty(thinking)) {
            content.Add(new DshWireContentBlock { Type = "reasoning", Text = thinking });
        }
        if (!string.IsNullOrEmpty(text)) {
            content.Add(new DshWireContentBlock { Type = "text", Text = text });
        }
        return (content, MapUsage(usage));
    }

    /// <summary>引擎 TokenUsage → 前端四桶（null 透传；未缓存输入 = prompt − cacheRead）</summary>
    private static DshTurnUsage? MapUsage(TokenUsage? usage) {
        if (usage is null) {
            return null;
        }
        var uncachedInput = Math.Max(0, usage.PromptTokens - usage.CacheReadInputTokens);
        return new DshTurnUsage {
            UncachedInputTokens = uncachedInput,
            OutputTokens = usage.CompletionTokens,
            CacheReadTokens = usage.CacheReadInputTokens,
            CacheWriteTokens = usage.CacheCreationInputTokens,
            TotalTokens = uncachedInput + usage.CompletionTokens + usage.CacheReadInputTokens + usage.CacheCreationInputTokens,
            ReasoningTokens = usage.ReasoningTokens,
        };
    }
}
