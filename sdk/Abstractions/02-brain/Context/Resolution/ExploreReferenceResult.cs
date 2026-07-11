
namespace JoinCode.Abstractions.Brain.Context.Resolution;

/// <summary>
/// 基于引用探索代码的结果
/// </summary>
public sealed record ExploreReferenceResult
{
    /// <summary>
    /// 是否成功
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// 解析后的引用信息
    /// </summary>
    public CodeReference? ResolvedReference { get; init; }

    /// <summary>
    /// 探索到的文件列表
    /// </summary>
    public IReadOnlyList<ExploredFile> Files { get; init; } = Array.Empty<ExploredFile>();

    /// <summary>
    /// 探索摘要
    /// </summary>
    public string? Summary { get; init; }

    /// <summary>
    /// 相关代码片段
    /// </summary>
    public IReadOnlyList<CodeSnippet> RelevantCodeSnippets { get; init; } = Array.Empty<CodeSnippet>();

    /// <summary>
    /// 探索 ID
    /// </summary>
    public string? ExploreId { get; init; }

    /// <summary>
    /// 执行时间（毫秒）
    /// </summary>
    public long ExecutionTimeMs { get; init; }

    /// <summary>
    /// Token 使用情况
    /// </summary>
    public TokenUsage TokenUsage { get; init; } = new();

    /// <summary>
    /// 错误信息（如果失败）
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// 是否成功解析引用
    /// </summary>
    public bool IsReferenceResolved => ResolvedReference?.IsResolved ?? false;

    /// <summary>
    /// 创建成功结果
    /// </summary>
    public static ExploreReferenceResult SuccessResult(
        CodeReference resolvedReference,
        IEnumerable<ExploredFile> files,
        string summary,
        string exploreId,
        long executionTimeMs,
        TokenUsage tokenUsage)
        => new()
        {
            Success = true,
            ResolvedReference = resolvedReference,
            Files = files.ToList(),
            Summary = summary,
            ExploreId = exploreId,
            ExecutionTimeMs = executionTimeMs,
            TokenUsage = tokenUsage
        };

    /// <summary>
    /// 创建失败结果
    /// </summary>
    public static ExploreReferenceResult FailureResult(string errorMessage)
        => new()
        {
            Success = false,
            ErrorMessage = errorMessage
        };
}

/// <summary>
/// 被探索的文件信息
/// </summary>
public sealed record ExploredFile
{
    /// <summary>
    /// 文件路径
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// 相对于项目根目录的路径
    /// </summary>
    public string? RelativePath { get; init; }

    /// <summary>
    /// 文件类型/扩展名
    /// </summary>
    public string? FileType { get; init; }

    /// <summary>
    /// 文件大小（字节）
    /// </summary>
    public long FileSize { get; init; }

    /// <summary>
    /// 最后修改时间
    /// </summary>
    public DateTime LastModified { get; init; }

    /// <summary>
    /// 文件内容（如果请求时包含）
    /// </summary>
    public string? Content { get; init; }

    /// <summary>
    /// 相关度评分
    /// </summary>
    public double RelevanceScore { get; init; }

    /// <summary>
    /// 匹配描述
    /// </summary>
    public string? MatchDescription { get; init; }
}

/// <summary>
/// 代码片段
/// </summary>
public sealed record CodeSnippet
{
    /// <summary>
    /// 来源文件路径
    /// </summary>
    public required string SourceFile { get; init; }

    /// <summary>
    /// 代码内容
    /// </summary>
    public required string Content { get; init; }

    /// <summary>
    /// 起始行号
    /// </summary>
    public int StartLine { get; init; }

    /// <summary>
    /// 结束行号
    /// </summary>
    public int EndLine { get; init; }

    /// <summary>
    /// 编程语言
    /// </summary>
    public string? Language { get; init; }

    /// <summary>
    /// 片段描述/上下文
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// 相关度评分
    /// </summary>
    public double RelevanceScore { get; init; }
}
