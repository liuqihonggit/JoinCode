
namespace Core.Prompts.Templates.Memory;

[PromptTemplate(Name = "compact", Category = PromptTemplateCategory.Memory, Description = "上下文压缩提示词模板，生成对话摘要", HasParameters = true)]
public static class CompactPromptTemplate
{
    private const string NoToolsPreamble = @"
重要：仅用文本回复。不要调用任何工具。

- 不要使用 Read、Bash、Grep、Glob、Edit、Write 或任何其他工具。
- 你在上面的对话中已经拥有所需的所有上下文。
- 工具调用将被拒绝，会浪费你唯一的机会 - 你会失败。
- 你的整个回复必须是纯文本：一个 <analysis> 块后跟一个 <summary> 块。

";

    private const string NoToolsTrailer = @"
提醒：不要调用任何工具。仅用纯文本回复 -
一个 <analysis> 块后跟一个 <summary> 块。
工具调用将被拒绝，你会失败。
";

    private const string DetailedAnalysisInstructionBase = @"
在提供最终摘要之前，将你的分析包裹在 <analysis> 标签中，以组织你的思路并确保你涵盖了所有必要要点。在你的分析过程中：

1. 按时间顺序分析对话的每个消息和部分。对每个部分彻底识别：
    - 用户的明确请求和意图
    - 你处理用户请求的方法
    - 关键决策、技术概念和代码模式
    - 具体细节，如：
        - 文件名
        - 完整代码片段
        - 函数签名
        - 文件编辑
    - 你遇到的错误以及如何修复它们
    - 特别注意你收到的具体用户反馈，尤其是如果用户告诉你以不同方式做某事。
2. 仔细检查技术准确性和完整性，彻底处理每个所需元素。
";

    private const string DetailedAnalysisInstructionPartial = @"
在提供最终摘要之前，将你的分析包裹在 <analysis> 标签中，以组织你的思路并确保你涵盖了所有必要要点。在你的分析过程中：

1. 按时间顺序分析最近的消息。对每个部分彻底识别：
    - 用户的明确请求和意图
    - 你处理用户请求的方法
    - 关键决策、技术概念和代码模式
    - 具体细节，如：
        - 文件名
        - 完整代码片段
        - 函数签名
        - 文件编辑
    - 你遇到的错误以及如何修复它们
    - 特别注意你收到的具体用户反馈，尤其是如果用户告诉你以不同方式做某事。
2. 仔细检查技术准确性和完整性，彻底处理每个所需元素。
";

    private const string BaseCompactPrompt = @"
你的任务是创建迄今为止对话的详细摘要，密切关注用户的明确请求和你之前的操作。
这个摘要应该彻底捕捉技术细节、代码模式和架构决策，这些对于在不丢失上下文的情况下继续开发工作至关重要。

{{DETAILED_ANALYSIS}}

你的摘要应包括以下部分：

1. 主要请求和意图：详细捕捉用户的所有明确请求和意图
2. 关键技术概念：列出讨论的所有重要技术概念、技术和框架。
3. 文件和代码部分：枚举检查、修改或创建的具体文件和代码部分。
特别注意最近的消息，并在适用的情况下包含完整代码片段，并包含为什么这个文件读取或编辑很重要的摘要。
4. 错误和修复：列出你遇到的所有错误，以及你是如何修复它们的。
特别注意你收到的具体用户反馈，尤其是如果用户告诉你以不同方式做某事。
5. 问题解决：记录已解决的问题和任何正在进行的故障排除工作。
6. 所有用户消息：列出所有不是工具结果的用户消息。这些对于理解用户的反馈和变化的意图至关重要。
7. 待处理任务：概述你被明确要求处理的任何待处理任务。
8. 当前工作：详细描述在此摘要请求之前正在处理的内容，特别注意用户和助理的最近消息。
在适用的情况下包含文件名和代码片段。
9. 可选下一步：列出与你正在做的最近工作相关的下一步。
重要：确保这一步与用户最近的明确请求直接一致，以及你在此摘要请求之前正在处理的任务。
如果你的最后一个任务已经结束，那么仅当它们明确符合用户请求时才列出下一步。
不要在没有先与用户确认的情况下开始切线请求或已经很旧且已完成的请求。
如果有下一步，包含最近对话中的直接引语，准确显示你正在处理什么任务以及你在哪里停止。
这应该是逐字的，以确保任务解释没有偏差。

以下是你的输出应该如何构建的示例：

<example>
<analysis>
[你的思考过程，确保彻底准确地涵盖所有要点]
</analysis>

<summary>
1. 主要请求和意图：
    [详细描述]

2. 关键技术概念：
    - [概念 1]
    - [概念 2]
    - [...]

3. 文件和代码部分：
    - [文件名 1]
        - [为什么这个文件很重要的摘要]
        - [对此文件所做更改的摘要（如果有）]
        - [重要代码片段]
    - [文件名 2]
        - [重要代码片段]
    - [...]

4. 错误和修复：
    - [错误 1 的详细描述]：
        - [你如何修复错误]
        - [用户对错误的反馈（如果有）]
    - [...]

5. 问题解决：
    [已解决问题的描述和正在进行的故障排除]

6. 所有用户消息：
    - [详细的非工具使用用户消息]
    - [...]

7. 待处理任务：
    - [任务 1]
    - [任务 2]
    - [...]

8. 当前工作：
    [当前工作的精确描述]

9. 可选下一步：
    [可选的下一步]

</summary>
</example>

请根据迄今为止的对话提供你的摘要，遵循这个结构并确保你的回复精确而彻底。

包含的上下文中可能提供了额外的摘要说明。如果有，请记住在创建上述摘要时遵循这些说明。说明示例包括：
<example>
## 压缩说明
在摘要对话时，重点关注 TypeScript 代码更改，还要记住你犯的错误以及如何修复它们。
</example>

<example>
# 摘要说明
当你使用 compact 时 - 请重点关注测试输出和代码更改。逐字包含文件读取。
</example>
";

    private const string PartialCompactPrompt = @"
你的任务是创建对话最近部分的详细摘要 — 跟随早期保留上下文的消息。
早期的消息保持完整，不需要摘要。仅将你的摘要集中在最近消息中讨论、学习和完成的内容上。

{{DETAILED_ANALYSIS}}

你的摘要应包括以下部分：

1. 主要请求和意图：捕捉最近消息中用户的明确请求和意图
2. 关键技术概念：列出最近讨论的重要技术概念、技术和框架。
3. 文件和代码部分：枚举检查、修改或创建的具体文件和代码部分。
在适用的情况下包含完整代码片段，并包含为什么这个文件读取或编辑很重要的摘要。
4. 错误和修复：列出的错误以及如何修复它们。
5. 问题解决：记录已解决的问题和任何正在进行的故障排除工作。
6. 所有用户消息：列出最近部分中所有不是工具结果的用户消息。
7. 待处理任务：概述最近消息中的任何待处理任务。
8. 当前工作：准确描述在此摘要请求之前立即正在处理的内容。
9. 可选下一步：列出与最近工作相关的下一步。包含最近对话中的直接引语。

以下是你的输出应该如何构建的示例：

<example>
<analysis>
[你的思考过程，确保彻底准确地涵盖所有要点]
</analysis>

<summary>
1. 主要请求和意图：
    [详细描述]

2. 关键技术概念：
    - [概念 1]
    - [概念 2]

3. 文件和代码部分：
    - [文件名 1]
        - [为什么这个文件很重要的摘要]
        - [重要代码片段]

4. 错误和修复：
    - [错误描述]：
        - [你如何修复它]

5. 问题解决：
    [描述]

6. 所有用户消息：
    - [详细的非工具使用用户消息]

7. 待处理任务：
    - [任务 1]

8. 当前工作：
    [当前工作的精确描述]

9. 可选下一步：
    [可选的下一步]

</summary>
</example>

请仅基于最近的消息（在保留的早期上下文之后）提供你的摘要，遵循这个结构并确保精确性和彻底性。
";

    private const string PartialCompactUpToPrompt = @"
你的任务是创建此对话的详细摘要。此摘要将放置在继续会话的开头；在此摘要之后将跟随基于此上下文的较新消息（你在此处看不到它们）。
请彻底摘要，以便仅阅读你的摘要然后阅读较新消息的人能够完全理解发生了什么并继续工作。

{{DETAILED_ANALYSIS}}

你的摘要应包括以下部分：

1. 主要请求和意图：详细捕捉用户的所有明确请求和意图
2. 关键技术概念：列出讨论的所有重要技术概念、技术和框架。
3. 文件和代码部分：枚举检查、修改或创建的具体文件和代码部分。
在适用的情况下包含完整代码片段，并包含为什么这个文件读取或编辑很重要的摘要。
4. 错误和修复：列出的错误以及如何修复它们。
5. 问题解决：记录已解决的问题和任何正在进行的故障排除工作。
6. 所有用户消息：列出所有不是工具结果的用户消息。
7. 待处理任务：概述任何待处理任务。
8. 已完成的工作：描述到此部分结束时完成了什么。
9. 继续工作的上下文：摘要理解后续消息中的工作并继续工作所需的任何上下文、决策或状态。

以下是你的输出应该如何构建的示例：

<example>
<analysis>
[你的思考过程，确保彻底准确地涵盖所有要点]
</analysis>

<summary>
1. 主要请求和意图：
    [详细描述]

2. 关键技术概念：
    - [概念 1]
    - [概念 2]

3. 文件和代码部分：
    - [文件名 1]
        - [为什么这个文件很重要的摘要]
        - [重要代码片段]

4. 错误和修复：
    - [错误描述]：
        - [你如何修复它]

5. 问题解决：
    [描述]

6. 所有用户消息：
    - [详细的非工具使用用户消息]

7. 待处理任务：
    - [任务 1]

8. 已完成的工作：
    [完成了什么的描述]

9. 继续工作的上下文：
    [继续工作所需的关键上下文、决策或状态]

</summary>
</example>

请根据此对话提供你的摘要，遵循这个结构并确保精确性和彻底性。
";

    public static string GetCompactPrompt(string? customInstructions = null)
    {
        var prompt = NoToolsPreamble + BaseCompactPrompt.Replace("{{DETAILED_ANALYSIS}}", DetailedAnalysisInstructionBase);

        if (!string.IsNullOrWhiteSpace(customInstructions))
        {
            prompt += $"\n\n额外说明：\n{customInstructions}";
        }

        prompt += NoToolsTrailer;
        return prompt;
    }

    public static string GetPartialCompactPrompt(
        string? customInstructions = null,
        CompactDirection direction = CompactDirection.From)
    {
        var template = direction == CompactDirection.UpTo
            ? PartialCompactUpToPrompt
            : PartialCompactPrompt;

        var analysisInstruction = direction == CompactDirection.UpTo
            ? DetailedAnalysisInstructionBase
            : DetailedAnalysisInstructionPartial;

        var prompt = NoToolsPreamble + template.Replace("{{DETAILED_ANALYSIS}}", analysisInstruction);

        if (!string.IsNullOrWhiteSpace(customInstructions))
        {
            prompt += $"\n\n额外说明：\n{customInstructions}";
        }

        prompt += NoToolsTrailer;
        return prompt;
    }

    private static readonly Regex AnalysisTagRegex = new(@"<analysis>[\u0000-\uffff]*?</analysis>", RegexOptions.Singleline);
    private static readonly Regex SummaryTagRegex = new(@"<summary>([\u0000-\uffff]*?)</summary>", RegexOptions.Singleline);
    private static readonly Regex MultipleBlankLinesRegex = new(@"\n\n+");

    public static string FormatCompactSummary(string summary)
    {
        var formattedSummary = AnalysisTagRegex.Replace(summary, "");

        var summaryMatch = SummaryTagRegex.Match(formattedSummary);

        if (summaryMatch.Success)
        {
            var content = summaryMatch.Groups[1].Value;
            formattedSummary = SummaryTagRegex.Replace(
                formattedSummary,
                $"摘要：\n{content.Trim()}");
        }

        formattedSummary = MultipleBlankLinesRegex.Replace(formattedSummary, "\n\n");

        return formattedSummary.Trim();
    }

    public static string GetCompactUserSummaryMessage(
        string summary,
        bool suppressFollowUpQuestions = false,
        string? transcriptPath = null,
        bool recentMessagesPreserved = false,
        bool isAutonomousMode = false)
    {
        var formattedSummary = FormatCompactSummary(summary);

        var baseSummary = $@"此会话正在从之前耗尽上下文的对话中继续。下面的摘要涵盖了对话的早期部分。

{formattedSummary}";

        if (!string.IsNullOrEmpty(transcriptPath))
        {
            baseSummary += $"\n\n如果你需要压缩前的具体细节（如确切的代码片段、错误消息或你生成的内容），请阅读完整记录：{transcriptPath}";
        }

        if (recentMessagesPreserved)
        {
            baseSummary += "\n\n最近的消息被逐字保留。";
        }

        if (suppressFollowUpQuestions)
        {
            var continuation = $@"{baseSummary}
从它停止的地方继续对话，不要再向用户询问任何问题。
直接恢复 - 不要确认摘要，不要回顾正在发生的事情，不要用 ""I'll continue"" 或类似的话作为开场白。
像中断从未发生一样继续最后一个任务。";

            if (isAutonomousMode)
            {
                continuation += @"

你正在自主/主动模式下运行。这不是首次唤醒 - 你在压缩之前就已经在自主工作了。继续你的工作循环：根据上面的摘要从你停止的地方继续。不要向用户问候或询问该做什么。";
            }

            return continuation;
        }

        return baseSummary;
    }
}
