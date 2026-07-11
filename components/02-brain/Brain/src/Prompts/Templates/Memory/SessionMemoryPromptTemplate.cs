
namespace Core.Prompts.Templates.Memory;

/// <summary>
/// 会话记忆提示词模板 - 用于更新会话笔记
/// </summary>
[PromptTemplate(Name = "session_memory", Category = PromptTemplateCategory.Memory, Description = "会话记忆更新提示词模板", HasParameters = true)]
public static class SessionMemoryPromptTemplate
{
    private const int MaxSectionLength = 2000;
    private const int MaxTotalSessionMemoryTokens = 12000;

    /// <summary>
    /// 默认会话记忆模板
    /// </summary>
    public const string DefaultSessionMemoryTemplate = """
        # 会话标题
        _会话的简短而有特色的 5-10 字描述性标题。信息超密集，无填充_

        # 当前状态
        _目前正在积极处理什么？尚未完成的待处理任务。立即的下一步。_

        # 任务规范
        _用户要求构建什么？任何设计决策或其他解释性上下文_

        # 文件和函数
        _重要的文件有哪些？简而言之，它们包含什么，为什么相关？_

        # 工作流
        _通常运行什么 bash 命令，按什么顺序？如果不是很明显，如何解释它们的输出？_

        # 错误与修正
        _遇到的错误以及如何修复。用户纠正了什么？哪些方法失败了，不应该再尝试？_

        # 代码库和系统文档
        _重要的系统组件有哪些？它们如何工作/如何配合？_

        # 学习心得
        _什么方法有效？什么无效？应该避免什么？不要重复其他部分的项目_

        # 关键结果
        _如果用户要求特定输出，如问题的答案、表格或其他文档，在此处重复确切结果_

        # 工作日志
        _一步一步，尝试了什么，完成了什么？每个步骤的非常简短的摘要_
        """;

    /// <summary>
    /// 获取默认更新提示词
    /// </summary>
    public static string GetDefaultUpdatePrompt(string notesPath, string currentNotes)
    {
        return $@"
            重要：此消息和这些说明不是实际用户对话的一部分。不要在笔记内容中包含任何对""记笔记""、""会话笔记提取""或这些更新说明的引用。

            基于上面的用户对话（不包括此记笔记说明消息以及系统提示、claude.md 条目或任何过去的会话摘要），更新会话笔记文件。

            文件 {notesPath} 已为你读取。以下是其当前内容：
            <current_notes_content>
            {currentNotes}
            </current_notes_content>

            你唯一的任务是使用 Edit 工具更新笔记文件，然后停止。你可以进行多次编辑（根据需要更新每个部分）- 在单个消息中并行进行所有 Edit 工具调用。不要调用任何其他工具。

            编辑的关键规则：
            - 文件必须保持其确切结构，所有部分、标题和斜体描述完整无缺
            -- 永远不要修改、删除或添加部分标题（以 '#' 开头的行，如 # 任务规范）
            -- 永远不要修改或删除斜体 _部分描述_ 行（这些是每个标题后立即以斜体显示的行 - 它们以 underscores 开头和结尾）
            -- 斜体 _部分描述_ 是必须完全保留的模板说明 - 它们指导每个部分应包含什么内容
            -- 只更新每个现有部分中斜体 _部分描述_ 下方出现的实际内容
            -- 不要在现有结构之外添加任何新部分、摘要或信息
            - 不要在笔记中的任何地方引用此记笔记过程或说明
            - 如果没有实质性的新见解要添加，可以跳过更新某个部分。不要添加像""暂无信息""这样的填充内容，如果合适，只需将部分留空/不编辑。
            - 为每个部分编写详细、信息密集的内容 - 包括具体信息，如文件路径、函数名、错误消息、确切命令、技术细节等。
            - 对于""关键结果""，包含用户请求的完整、确切输出（例如完整表格、完整答案等）
            - 不要包含已包含在上下文中的 CLAUDE.md 文件中的信息
            - 将每个部分保持在 ~{MaxSectionLength} 个词/字以下 - 如果某个部分接近此限制，通过循环掉不太重要的细节来压缩它，同时保留最关键的信息
            - 关注可操作的、具体的信息，这些信息将帮助某人理解或重现对话中讨论的工作
            - 重要：始终更新""当前状态""以反映最近的工作 - 这对于压缩后的连续性至关重要

            使用 Edit 工具，file_path: {notesPath}

            结构保留提醒：
            每个部分有两个必须完全保留的部分：
            1. 部分标题（以 # 开头的行）
            2. 斜体描述行（标题后立即以斜体显示的 _italicized text_ - 这是模板说明）

            你只更新这两行保留行之后出现的实际内容。以 underscores 开头和结尾的斜体描述行是模板结构的一部分，不是要编辑或删除的内容。

            记住：并行使用 Edit 工具并停止。编辑后不要继续。只包含来自实际用户对话的见解，永远不要来自这些说明。不要删除或更改部分标题或斜体 _部分描述_。
            ";
    }

    /// <summary>
    /// 分析各部分大小
    /// </summary>
    public static Dictionary<string, int> AnalyzeSectionSizes(string content)
    {
        var sections = new Dictionary<string, int>();
        var lines = content.Split('\n');
        var currentSection = "";
        var currentContent = new List<string>();

        foreach (var line in lines)
        {
            if (line.StartsWith("# "))
            {
                if (!string.IsNullOrEmpty(currentSection) && currentContent.Count > 0)
                {
                    var sectionContent = string.Join("\n", currentContent).Trim();
                    sections[currentSection] = RoughTokenCountEstimation(sectionContent);
                }
                currentSection = line;
                currentContent = [];
            }
            else
            {
                currentContent.Add(line);
            }
        }

        if (!string.IsNullOrEmpty(currentSection) && currentContent.Count > 0)
        {
            var sectionContent = string.Join("\n", currentContent).Trim();
            sections[currentSection] = RoughTokenCountEstimation(sectionContent);
        }

        return sections;
    }

    /// <summary>
    /// 生成部分大小提醒
    /// </summary>
    public static string GenerateSectionReminders(Dictionary<string, int> sectionSizes, int totalTokens)
    {
        var overBudget = totalTokens > MaxTotalSessionMemoryTokens;
        var oversizedSections = sectionSizes
            .Where(x => x.Value > MaxSectionLength)
            .OrderByDescending(x => x.Value)
            .Select(x => $"- \"{x.Key}\" 约为 {x.Value} 个词（限制：{MaxSectionLength}）")
            .ToList();

        if (oversizedSections.Count == 0 && !overBudget)
        {
            return "";
        }

        var parts = new List<string>();

        if (overBudget)
        {
            parts.Add($@"
                关键：会话记忆文件当前约为 {totalTokens} 个词，超过了 {MaxTotalSessionMemoryTokens} 个词的最大值。你必须压缩文件以适应此预算。通过删除不太重要的细节、合并相关项目和摘要旧条目来积极缩短超长的部分。优先保持""当前状态""和""错误与修正""准确而详细。
                ");
        }

        if (oversizedSections.Count > 0)
        {
            parts.Add($@"
                {(overBudget ? "需要压缩的超长部分" : "重要：以下部分超过了每部分限制，必须压缩")}：
                {string.Join("\n", oversizedSections)}
                ");
        }

        return string.Join("", parts);
    }

    /// <summary>
    /// 粗略估算 token 数量
    /// </summary>
    private static int RoughTokenCountEstimation(string text)
    {
        // 简单估算：每4个字符约1个token
        return text.Length / 4;
    }

    /// <summary>
    /// 构建会话记忆更新提示词
    /// </summary>
    public static string BuildSessionMemoryUpdatePrompt(string currentNotes, string notesPath)
    {
        var prompt = GetDefaultUpdatePrompt(notesPath, currentNotes);

        // 分析部分大小并生成提醒
        var sectionSizes = AnalyzeSectionSizes(currentNotes);
        var totalTokens = RoughTokenCountEstimation(currentNotes);
        var sectionReminders = GenerateSectionReminders(sectionSizes, totalTokens);

        return prompt + sectionReminders;
    }

    /// <summary>
    /// 截断会话记忆用于压缩
    /// </summary>
    public static (string TruncatedContent, bool WasTruncated) TruncateSessionMemoryForCompact(string content)
    {
        var lines = content.Split('\n');
        var maxCharsPerSection = MaxSectionLength * 4;
        var outputLines = new List<string>();
        var currentSectionLines = new List<string>();
        var currentSectionHeader = "";
        var wasTruncated = false;

        foreach (var line in lines)
        {
            if (line.StartsWith("# "))
            {
                var result = FlushSessionSection(currentSectionHeader, currentSectionLines, maxCharsPerSection);
                outputLines.AddRange(result.Lines);
                wasTruncated = wasTruncated || result.WasTruncated;
                currentSectionHeader = line;
                currentSectionLines = [];
            }
            else
            {
                currentSectionLines.Add(line);
            }
        }

        // 刷新最后一部分
        var finalResult = FlushSessionSection(currentSectionHeader, currentSectionLines, maxCharsPerSection);
        outputLines.AddRange(finalResult.Lines);
        wasTruncated = wasTruncated || finalResult.WasTruncated;

        return (string.Join("\n", outputLines), wasTruncated);
    }

    private static (List<string> Lines, bool WasTruncated) FlushSessionSection(
        string sectionHeader,
        List<string> sectionLines,
        int maxCharsPerSection)
    {
        if (string.IsNullOrEmpty(sectionHeader))
        {
            return (sectionLines, false);
        }

        var sectionContent = string.Join("\n", sectionLines);
        if (sectionContent.Length <= maxCharsPerSection)
        {
            var lines = new List<string> { sectionHeader };
            lines.AddRange(sectionLines);
            return (lines, false);
        }

        // 在接近限制的行边界处截断
        var charCount = 0;
        var keptLines = new List<string> { sectionHeader };
        foreach (var line in sectionLines)
        {
            if (charCount + line.Length + 1 > maxCharsPerSection)
            {
                break;
            }
            keptLines.Add(line);
            charCount += line.Length + 1;
        }
        keptLines.Add("\n[... 部分因长度被截断 ...]");
        return (keptLines, true);
    }
}
