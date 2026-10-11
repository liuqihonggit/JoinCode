namespace Core.Prompts.Sections;

/// <summary>
/// 简化模式部分 - 极简系统提示词
/// </summary>
[PromptSection(Name = "simple_mode", Order = 24, IsDynamic = true)]
public static class SimpleModeSection {
    /// <summary>
    /// 获取简化模式的提示词内容。当文件系统不可用时返回 null。
    /// </summary>
    /// <returns>极简系统提示词文本；若文件系统不可用则返回 null。</returns>
    public static string? GetContent() {
        var snapshot = PromptConfigSnapshot.Current;
        var fs = snapshot.FileSystem;
        if (fs is null) return null;

        var cwd = fs.GetCurrentDirectory();
        var now = snapshot.Clock?.GetLocalNow() ?? DateTime.Now;
        var date = now.ToString("yyyy-MM-dd");

        return $"""
            您是 JoinCode，一个AI驱动的软件工程助手。

            CWD: {cwd}
            Date: {date}
            """;
    }

    /// <summary>
    /// 创建简化模式 Section 实例（动态内容，每次重新生成）。
    /// </summary>
    /// <returns>简化模式 Section 实例。</returns>
    public static SystemPromptSection Create() =>
        SystemPromptSection.Dynamic("simple_mode", GetContent);
}