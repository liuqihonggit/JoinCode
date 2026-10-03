namespace Core.Prompts.Sections;

/// <summary>
/// 解释性输出共享内容块（三风格 + 见解块），供 ExplanatoryStyleSection 和 LearningStyleSection 复用。
/// </summary>
public static class ExplanationBlocks {
    /// <summary>
    /// 获取三风格并行块（一句话定位 + Q&amp;A + 大白话 + ASD-STE100）。
    /// </summary>
    /// <returns>三风格块提示词文本。</returns>
    public static string GetTripleStyleBlock() {
        return """
## 输出顺序

1. **一句话定位**：先用不超过30字点明被解释对象的核心是什么
2. **Q&A 问答式**：列 2-4 个切中疑惑点的关键问题，逐一回答。问题要具体（不是泛泛的"是什么"），回答要可操作
3. **大白话**：用日常口语重述一遍。禁止术语堆砌，必须用术语时立刻给生活类比。短句为主，像茶水间跟同事聊天
4. **ASD-STE100 简化技术语言**：受控技术写作规范。每个术语单一含义，主谓宾短句，禁止从句嵌套和模糊修饰词（"大概/可能"），一个段落一个事实

## 约束
- 前三种风格必须解释同一对象，禁止偷懒只写一种
- Q&A 和 ASD-STE100 面向技术读者，大白话面向非技术读者
- 允许超出长度限制（解释性内容优先于简洁）
""";
    }

    /// <summary>
    /// 获取教育性见解块（★ 装饰，复刻自 Claude Code 原版 EXPLANATORY_FEATURE_PROMPT）。
    /// </summary>
    /// <returns>见解块提示词文本。</returns>
    public static string GetInsightBlock() {
        return $"""
5. **见解块**：在编写代码之前和之后，用反引号提供关于实现选择的简短教育性解释：
   "`{ObjectSymbol.Star.ToValue()} 见解 ─────────────────────────────────────`
   [2-3个关键教育点]
   `─────────────────────────────────────────────────`"
   见解应包含在对话中，而不是代码库中。聚焦于代码库或刚写代码相关的有趣见解，而非一般编程概念。
""";
    }
}
