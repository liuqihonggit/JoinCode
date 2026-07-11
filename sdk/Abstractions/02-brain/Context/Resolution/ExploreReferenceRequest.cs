namespace JoinCode.Abstractions.Brain.Context.Resolution;

/// <summary>
/// 基于引用探索代码的请求参数
/// </summary>
public sealed record ExploreReferenceRequest
{
    /// <summary>
    /// 引用路径，如 "claude-code/src/tools" 或 "工具实现"
    /// </summary>
    public required string ReferencePath { get; init; }

    /// <summary>
    /// 搜索深度，默认为 3 层
    /// </summary>
    public int SearchDepth { get; init; } = 3;

    /// <summary>
    /// 是否包含子目录，默认为 true
    /// </summary>
    public bool IncludeSubdirectories { get; init; } = true;

    /// <summary>
    /// 关注领域/特定焦点，用于细化探索范围
    /// </summary>
    public string? FocusArea { get; init; }

    /// <summary>
    /// 需要回答的具体问题列表
    /// </summary>
    public List<string>? Questions { get; init; }

    /// <summary>
    /// 最大返回文件数量，默认为 20
    /// </summary>
    public int MaxFiles { get; init; } = 20;

    /// <summary>
    /// 是否包含文件内容，默认为 true
    /// </summary>
    public bool IncludeFileContent { get; init; } = true;

    /// <summary>
    /// 创建默认请求
    /// </summary>
    public static ExploreReferenceRequest Create(string referencePath)
        => new()
        {
            ReferencePath = referencePath
        };

    /// <summary>
    /// 创建带关注领域的请求
    /// </summary>
    public static ExploreReferenceRequest CreateWithFocus(string referencePath, string focusArea)
        => new()
        {
            ReferencePath = referencePath,
            FocusArea = focusArea
        };
}
