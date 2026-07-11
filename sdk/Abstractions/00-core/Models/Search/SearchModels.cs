namespace JoinCode.Abstractions.Models.Search;

/// <summary>
/// Glob 搜索结果
/// </summary>
public sealed record GlobSearchResult
{
    public required long DurationMs { get; init; }
    public required int NumFiles { get; init; }
    public required IReadOnlyList<string> Filenames { get; init; }
    public required bool Truncated { get; init; }
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }

    public static GlobSearchResult SuccessResult(
        long durationMs,
        IReadOnlyList<string> filenames,
        bool truncated)
        => new()
        {
            DurationMs = durationMs,
            NumFiles = filenames.Count,
            Filenames = filenames,
            Truncated = truncated,
            Success = true
        };

    public static GlobSearchResult FailureResult(string errorMessage)
        => new()
        {
            DurationMs = 0,
            NumFiles = 0,
            Filenames = Array.Empty<string>(),
            Truncated = false,
            Success = false,
            ErrorMessage = errorMessage
        };
}

/// <summary>
/// 搜索输出模式 — 替代 GrepSearchInput.OutputMode 的字符串常量
/// </summary>
public enum SearchOutputMode
{
    [EnumValue("files_with_matches")] Files = 0,
    [EnumValue("content")] Content = 1,
    [EnumValue("count")] Count = 2
}

/// <summary>
/// Grep 搜索输入参数
/// </summary>
public sealed record GrepSearchInput
{
    /// <summary>
    /// 搜索模式（正则表达式）
    /// </summary>
    public required string Pattern { get; init; }

    /// <summary>
    /// 搜索路径
    /// </summary>
    public string? Path { get; init; }

    /// <summary>
    /// Glob 过滤模式
    /// </summary>
    public string? Glob { get; init; }

    /// <summary>
    /// 输出模式
    /// </summary>
    public SearchOutputMode OutputMode { get; init; } = SearchOutputMode.Files;

    /// <summary>
    /// 匹配行前显示的行数
    /// </summary>
    public int? Before { get; init; }

    /// <summary>
    /// 匹配行后显示的行数
    /// </summary>
    public int? After { get; init; }

    /// <summary>
    /// 匹配行前后显示的行数（简写）
    /// </summary>
    public int? Context { get; init; }

    /// <summary>
    /// 是否显示行号
    /// </summary>
    public bool LineNumbers { get; init; } = true;

    /// <summary>
    /// 是否忽略大小写
    /// </summary>
    public bool CaseInsensitive { get; init; } = false;

    /// <summary>
    /// 文件类型过滤
    /// </summary>
    public string? FileType { get; init; }

    /// <summary>
    /// 结果数量限制
    /// </summary>
    public int? HeadLimit { get; init; }

    /// <summary>
    /// 结果偏移量
    /// </summary>
    public int? Offset { get; init; }

    /// <summary>
    /// 是否启用多行模式
    /// </summary>
    public bool Multiline { get; init; } = false;

    /// <summary>
    /// Read deny 规则的排除模式 — 对齐 TS getFileReadIgnorePatterns
    /// 从 PathPermissionChecker.GetReadDenyPatterns() 获取
    /// 搜索时排除匹配这些模式的文件
    /// </summary>
    public IReadOnlyList<string>? DenyPatterns { get; init; }
}

/// <summary>
/// Grep 搜索结果
/// </summary>
public sealed record GrepSearchResult
{
    public string? Mode { get; init; }
    public required int NumFiles { get; init; }
    public required IReadOnlyList<string> Filenames { get; init; }
    public string? Content { get; init; }
    public int? NumLines { get; init; }
    public int? NumMatches { get; init; }
    public int? AppliedLimit { get; init; }
    public int? AppliedOffset { get; init; }
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }

    public static GrepSearchResult SuccessResult(
        string? mode,
        IReadOnlyList<string> filenames,
        string? content = null,
        int? numLines = null,
        int? numMatches = null,
        int? appliedLimit = null,
        int? appliedOffset = null)
        => new()
        {
            Mode = mode,
            NumFiles = filenames.Count,
            Filenames = filenames,
            Content = content,
            NumLines = numLines,
            NumMatches = numMatches,
            AppliedLimit = appliedLimit,
            AppliedOffset = appliedOffset,
            Success = true
        };

    public static GrepSearchResult FailureResult(string errorMessage)
        => new()
        {
            Mode = null,
            NumFiles = 0,
            Filenames = Array.Empty<string>(),
            Content = null,
            NumLines = null,
            NumMatches = null,
            AppliedLimit = null,
            AppliedOffset = null,
            Success = false,
            ErrorMessage = errorMessage
        };
}
