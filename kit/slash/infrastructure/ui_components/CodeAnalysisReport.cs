namespace JoinCode.ChatCommands;

/// <summary>
/// 代码分析报告 — 描述一次代码分析的整体结果
/// </summary>
public sealed class CodeAnalysisReport {
    /// <summary>
    /// 分析目标路径
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// 文件总数
    /// </summary>
    public int TotalFiles { get; set; }

    /// <summary>
    /// 按扩展名分组的文件类型条目列表
    /// </summary>
    public List<FileTypeEntry> FileTypes { get; set; } = new();

    /// <summary>
    /// 分析执行时间
    /// </summary>
    public DateTime AnalyzedAt { get; set; }
}

/// <summary>
/// 文件类型条目 — 单个扩展名对应的文件计数
/// </summary>
public sealed class FileTypeEntry {
    /// <summary>
    /// 文件扩展名
    /// </summary>
    public string Extension { get; set; } = string.Empty;

    /// <summary>
    /// 该扩展名的文件数量
    /// </summary>
    public int Count { get; set; }
}