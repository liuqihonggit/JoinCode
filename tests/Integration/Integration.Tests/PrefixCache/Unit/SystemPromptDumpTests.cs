namespace Integration.Tests.PrefixCache.Unit;

public sealed class SystemPromptDumpTests
{
    [Fact]
    public void Dump_AllSections_ToFile()
    {
        var fs = new IO.FileSystem.InMemoryFileSystem();
        var options = new SystemPromptProviderOptions
        {
            EnabledTools = ["Bash", "Read", "Write", "Edit", "Glob", "Grep", "Task"],
            ModelId = "deepseek-v4-flash",
            ModelName = "DeepSeek V4 Flash",
            Version = "1.0.0-test",
            BuildTime = "2026-06-24",
            LanguagePreference = "简体中文"
        };

        var provider = new DefaultSystemPromptProvider(fs, options);
        var builder = new SystemPromptBuilder();
        builder.AddFromProvider(provider);

        var (staticPrefix, dynamicSuffix) = builder.BuildPartitioned();

        var sb = new StringBuilder();
        sb.AppendLine("# 系统提示词完整导出");
        sb.AppendLine($"导出时间: {DateTimeOffset.UtcNow:O}");
        sb.AppendLine();

        sb.AppendLine("## 静态前缀 (Static Prefix)");
        sb.AppendLine("用于前缀缓存，会话期间不变");
        sb.AppendLine();
        sb.AppendLine(staticPrefix);
        sb.AppendLine();

        sb.AppendLine("## 动态后缀 (Dynamic Suffix)");
        sb.AppendLine("每轮可能变化，不影响静态前缀缓存");
        sb.AppendLine();
        sb.AppendLine(dynamicSuffix);

        var sections = provider.GetSections().ToList();
        sb.AppendLine();
        sb.AppendLine("## Section 清单");
        sb.AppendLine();
        sb.AppendLine("| # | Name | CacheBreak | Content Length |");
        sb.AppendLine("|---|------|------------|---------------|");
        for (var i = 0; i < sections.Count; i++)
        {
            var s = sections[i];
            var content = s.Compute();
            sb.AppendLine($"| {i + 1} | {s.Name} | {(s.CacheBreak ? "Dynamic" : "Cached")} | {content?.Length ?? 0} |");
        }

        var dir = fs.CombinePath(AppContext.BaseDirectory, ".x");
        if (!fs.DirectoryExists(dir)) fs.CreateDirectory(dir);
        var filePath = fs.CombinePath(dir, "system_prompt_dump.md");
        fs.WriteAllText(filePath, sb.ToString());

        staticPrefix.Should().NotBeEmpty("静态前缀应包含内容");
    }
}
