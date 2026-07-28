using JoinCode.Abstractions.Attributes;
using Core.Prompts;

namespace Core.Prompts.Sections;

/// <summary>
/// Companion (Buddy) 提示词部分
/// </summary>
[PromptSection(Name = "companion", Order = 15)]
public static class CompanionSection
{
    /// <summary>
    /// 获取伙伴介绍文本
    /// </summary>
    public static string? GetContent()
    {
        var name = PromptConfigSnapshot.Current.CompanionName;
        var species = PromptConfigSnapshot.Current.CompanionSpecies;
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(species))
            return null;
        return GetCompanionIntroText(name, species);
    }

    /// <summary>
    /// 获取伙伴介绍文本
    /// </summary>
    public static string GetCompanionIntroText(string name, string species)
    {
        return $@"# 伙伴

一只名叫 {name} 的小 {species} 坐在用户输入框旁边，偶尔会在气泡中评论。

你不是 {name} - 它是一个单独的观察者。

当用户直接称呼 {name}（通过名字）时，它的气泡会回答。

你在那一刻的工作是让开：用一行或更少回复，或者只回答消息中针对你的部分。

不要解释你不是 {name} - 他们知道。不要叙述 {name} 可能会说什么 - 气泡会处理那个。
";
    }

    public static SystemPromptSection Create() =>
        SystemPromptSection.Dynamic("companion", GetContent);
}
