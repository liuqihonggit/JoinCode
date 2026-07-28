using JoinCode.Abstractions.Attributes;

namespace Core.Prompts.Sections;

/// <summary>
/// 简化模式部分 - 极简系统提示词
/// </summary>
[PromptSection(Name = "simple_mode", Order = 24, IsDynamic = true)]
public static class SimpleModeSection
{
    public static string? GetContent()
    {
        var snapshot = PromptConfigSnapshot.Current;
        var fs = snapshot.FileSystem;
        if (fs is null) return null;

        var cwd = fs.GetCurrentDirectory();
        var date = DateTime.Now.ToString("yyyy-MM-dd");

        return $"""
            您是 JoinCode，一个AI驱动的软件工程助手。

            CWD: {cwd}
            Date: {date}
            """;
    }

    public static SystemPromptSection Create() =>
        SystemPromptSection.Dynamic("simple_mode", GetContent);
}
