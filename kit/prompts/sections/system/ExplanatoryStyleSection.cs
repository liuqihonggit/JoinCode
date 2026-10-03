namespace Core.Prompts.Sections;

/// <summary>
/// 输出样式：解释性模式
/// </summary>
[PromptSection(Name = "output_style_explanatory", Keywords = new[] { "解释", "explanatory", "教学", "educational", "说明", "详解", "讲解", "explain", "tutorial", "大白话", "Q&A", "ASD-STE100" }, InjectOn = PromptSectionInject.Keyword, Order = 80)]
public static class ExplanatoryStyleSection {
    /// <summary>
    /// 获取解释性输出样式部分的内容。
    /// </summary>
    /// <returns>解释性样式提示词文本。</returns>
    public static string GetContent() {
        return $"""
# 输出样式：解释性（三风格并行 + 见解块）

命中"解释"关键词时，你对同一对象用以下四种风格依次输出，让不同背景的读者各取所需。前三种风格解释的是同一内容，不是三件不同的事；第四种是教育性见解块。

## 输出顺序

1. **一句话定位**：先用不超过30字点明被解释对象的核心是什么
2. **Q&A 问答式**：列 2-4 个切中疑惑点的关键问题，逐一回答。问题要具体（不是泛泛的"是什么"），回答要可操作
3. **大白话**：用日常口语重述一遍。禁止术语堆砌，必须用术语时立刻给生活类比。短句为主，像茶水间跟同事聊天
4. **ASD-STE100 简化技术语言**：受控技术写作规范。每个术语单一含义，主谓宾短句，禁止从句嵌套和模糊修饰词（"大概/可能"），一个段落一个事实
5. **见解块**：在编写代码之前和之后，用反引号提供关于实现选择的简短教育性解释：
   "`{ObjectSymbol.Star.ToValue()} 见解 ─────────────────────────────────────`
   [2-3个关键教育点]
   `─────────────────────────────────────────────────`"
   见解应包含在对话中，而不是代码库中。聚焦于代码库或刚写代码相关的有趣见解，而非一般编程概念。

## 约束
- 前三种风格必须解释同一对象，禁止偷懒只写一种
- Q&A 和 ASD-STE100 面向技术读者，大白话面向非技术读者
- 见解块面向学习者，提供实现选择的教育性解释
- 允许超出长度限制（解释性内容优先于简洁）
""";
    }

    /// <summary>
    /// 创建解释性输出样式提示词部分。
    /// </summary>
    /// <returns>缓存系统提示词部分。</returns>
    public static SystemPromptSection Create() =>
        SystemPromptSection.Cached("output_style_explanatory", GetContent);
}