using JoinCode.Abstractions.Attributes;
using Core.Prompts;

namespace Core.Prompts.Sections;

/// <summary>
/// 记忆部分 - 关于对话记忆系统
/// </summary>
[PromptSection(Name = "memory", Order = 77, IsDynamic = true)]
public static class MemorySection
{
    public static async Task<string?> GetContentAsync()
    {
        var fs = PromptConfigSnapshot.Current.FileSystem;
        if (fs is null) return null;

        var dailyLogPromptBuilder = PromptConfigSnapshot.Current.DailyLogPromptBuilder;
        var searchHistoryPromptBuilder = PromptConfigSnapshot.Current.SearchHistoryPromptBuilder;

        var memories = LoadMemories(fs);
        var memoryContent = string.IsNullOrEmpty(memories)
            ? "[暂无记忆]"
            : memories;

        var sb = new StringBuilder();
        sb.AppendLine($"""
# 记忆

{memoryContent}
""");

        if (dailyLogPromptBuilder is not null)
        {
            try
            {
                var dailyLogPrompt = await dailyLogPromptBuilder().ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(dailyLogPrompt))
                {
                    sb.AppendLine();
                    sb.AppendLine(dailyLogPrompt);
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine();
                sb.AppendLine($"[助手日志加载失败: {ex.Message}]");
            }
        }

        if (searchHistoryPromptBuilder is not null)
        {
            try
            {
                var searchHistoryPrompt = await searchHistoryPromptBuilder(string.Empty).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(searchHistoryPrompt))
                {
                    sb.AppendLine();
                    sb.AppendLine(searchHistoryPrompt);
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine();
                sb.AppendLine($"[搜索历史加载失败: {ex.Message}]");
            }
        }

        sb.AppendLine("""
## 何时访问记忆

- 当记忆看起来相关，或用户引用之前对话的工作时。
- 当用户明确要求你检查、回忆或记住时。
- 如果用户说*忽略*或*不使用*记忆：就像 MEMORY.md 是空的一样继续。不要应用记住的事实、引用、与记忆内容比较或提及记忆内容。
- 记忆记录可能随时间变得陈旧。将记忆用作某时某刻为真的上下文。在回答用户或仅基于记忆信息建立假设之前，通过读取文件或资源的当前状态来验证记忆是否仍然正确和最新。如果回忆的记忆与当前信息冲突，相信你现在观察到的——并更新或删除陈旧的记忆，而不是基于它行动。

## 在根据记忆推荐之前

命名特定函数、文件或标志的记忆是声称它*在记忆写入时*存在。它可能已被重命名、删除或从未合并。在推荐之前：

- 如果记忆命名文件路径：检查文件是否存在。
- 如果记忆命名函数或标志：搜索它。
- 如果用户即将根据你的推荐行动（不仅仅是询问历史），先验证。

""记忆说X存在""不同于""X现在存在""。

总结仓库状态的记忆（活动日志、架构快照）冻结在时间中。如果用户询问*最近*或*当前*状态，优先使用 `git log` 或读取代码，而不是回忆快照。
""");

        return sb.ToString();
    }

    public static SystemPromptSection Create() =>
        SystemPromptSection.Dynamic("memory", GetContentAsync);

    /// <summary>
    /// 从持久化存储加载记忆
    /// </summary>
    private static string LoadMemories(IFileSystem fs)
    {
        var memoryPaths = new[]
        {
            Path.Combine(Environment.CurrentDirectory, "MEMORY.md"),
            Path.Combine(Environment.CurrentDirectory, AppDataConstants.AppDataFolder, "MEMORY.md"),
        };

        foreach (var path in memoryPaths)
        {
            try
            {
                if (fs.FileExists(path))
                {
                    var content = fs.ReadAllText(path);
                    if (!string.IsNullOrWhiteSpace(content))
                    {
                        return $"""
以下信息是从之前的对话中提取的相关记忆：

{content.Trim()}
""";
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"Memory section content loading failed: {ex.Message}");
            }
        }

        return """
以下信息是从之前的对话中提取的相关记忆：

[记忆内容将在运行时动态加载]
""";
    }

    /// <summary>
    /// 创建记忆访问时机部分
    /// </summary>
    public static SystemPromptSection CreateWhenToAccessSection()
    {
        return SystemPromptSection.Cached("memory_when_to_access", () => """
## 何时访问记忆

- 当记忆看起来相关，或用户引用之前对话的工作时。
- 当用户明确要求你检查、回忆或记住时。
- 如果用户说*忽略*或*不使用*记忆：就像 MEMORY.md 是空的一样继续。不要应用记住的事实、引用、与记忆内容比较或提及记忆内容。
- 记忆记录可能随时间变得陈旧。将记忆用作某时某刻为真的上下文。在回答用户或仅基于记忆信息建立假设之前，通过读取文件或资源的当前状态来验证记忆是否仍然正确和最新。如果回忆的记忆与当前信息冲突，相信你现在观察到的——并更新或删除陈旧的记忆，而不是基于它行动。
""");
    }

    /// <summary>
    /// 创建记忆信任指导部分
    /// </summary>
    public static SystemPromptSection CreateTrustingRecallSection()
    {
        return SystemPromptSection.Cached("memory_trusting_recall", () => """
## 在根据记忆推荐之前

命名特定函数、文件或标志的记忆是声称它*在记忆写入时*存在。它可能已被重命名、删除或从未合并。在推荐之前：

- 如果记忆命名文件路径：检查文件是否存在。
- 如果记忆命名函数或标志：搜索它。
- 如果用户即将根据你的推荐行动（不仅仅是询问历史），先验证。

"记忆说X存在"不同于"X现在存在"。

总结仓库状态的记忆（活动日志、架构快照）冻结在时间中。如果用户询问*最近*或*当前*状态，优先使用 `git log` 或读取代码，而不是回忆快照。
""");
    }
}
