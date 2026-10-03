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

{ExplanationBlocks.GetTripleStyleBlock()}

{ExplanationBlocks.GetInsightBlock()}
""";
    }

    /// <summary>
    /// 创建解释性输出样式提示词部分。
    /// </summary>
    /// <returns>缓存系统提示词部分。</returns>
    public static SystemPromptSection Create() =>
        SystemPromptSection.Cached("output_style_explanatory", GetContent);
}