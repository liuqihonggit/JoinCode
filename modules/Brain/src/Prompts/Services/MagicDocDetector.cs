
namespace Core.Prompts.Services;

/// <summary>
/// Magic Doc 检测结果
/// </summary>
public sealed class MagicDocDetection
{
    /// <summary>
    /// 文档标题（# MAGIC DOC: 后面的内容）
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// 自定义更新指令（标题下一行的斜体文本，可选）
    /// </summary>
    public string? CustomInstructions { get; init; }
}

/// <summary>
/// Magic Doc 追踪条目
/// </summary>
public sealed class MagicDocEntry
{
    /// <summary>
    /// 文件路径
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// 文档标题
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// 自定义更新指令
    /// </summary>
    public string? CustomInstructions { get; init; }
}

/// <summary>
/// Magic Doc 检测器 — 检测文件内容中的 # MAGIC DOC: 头部
/// 对齐 TS magicDocs.ts::detectMagicDocHeader
/// </summary>
public static class MagicDocDetector
{
    private static readonly Regex HeaderPattern = new(@"^#\s*MAGIC\s+DOC:\s*(.+)$", RegexOptions.Multiline | RegexOptions.IgnoreCase);
    private static readonly Regex ItalicInstructionPattern = new(@"^_(.+)_$", RegexOptions.Multiline);

    /// <summary>
    /// 检测文件内容是否包含 Magic Doc 头部
    /// </summary>
    public static MagicDocDetection? Detect(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return null;

        var match = HeaderPattern.Match(content);
        if (!match.Success || match.Groups.Count < 2) return null;

        var title = match.Groups[1].Value.Trim();
        if (string.IsNullOrEmpty(title)) return null;

        string? customInstructions = null;
        var afterHeader = content.Substring(match.Index + match.Length);
        var italicMatch = ItalicInstructionPattern.Match(afterHeader.TrimStart());
        if (italicMatch.Success && italicMatch.Index < 5)
        {
            customInstructions = italicMatch.Groups[1].Value.Trim();
        }

        return new MagicDocDetection { Title = title, CustomInstructions = customInstructions };
    }
}
