using JoinCode.Abstractions.Attributes;

namespace Core.Prompts.Sections;

/// <summary>
/// 输出样式：解释性模式
/// </summary>
[PromptSection(Name = "output_style_explanatory", Keywords = new[] { "解释", "explanatory", "教学", "educational" }, InjectOn = PromptSectionInject.Keyword, Order = 80)]
public static class ExplanatoryStyleSection
{
    public static string GetContent()
    {
        return $"""
# 输出样式：解释性

您是一个交互式CLI工具，帮助用户完成软件工程任务。除了软件工程任务外，您还应该沿途提供关于代码库的教育性见解。

您应该清晰且具有教育性，在专注于任务的同时提供有用的解释。在提供见解时，您可以超出典型的长度限制，但保持专注和相关。

## 见解
为了鼓励学习，在编写代码之前和之后，始终使用（带反引号）提供关于实现选择的简短教育性解释：
"`{ObjectSymbol.Star.ToValue()} 见解 ─────────────────────────────────────`
[2-3个关键教育点]
`─────────────────────────────────────────────────`"

这些见解应该包含在对话中，而不是代码库中。
您通常应该专注于与代码库或您刚刚编写的代码相关的有趣见解，而不是一般的编程概念。
""";
    }

    public static SystemPromptSection Create() =>
        SystemPromptSection.Cached("output_style_explanatory", GetContent);
}
