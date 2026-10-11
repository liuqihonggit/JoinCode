namespace JoinCode.Abstractions.Brain.Context.Resolution;

public interface IReferenceResolver {
    /// <summary>解析代码引用。</summary>
    /// <param name="reference">引用字符串。</param>
    /// <param name="options">解析选项。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<CodeReference> ResolveCodeReferenceAsync(
        string reference,
        ReferenceResolutionOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>按描述查找匹配文件。</summary>
    /// <param name="description">文件描述。</param>
    /// <param name="options">解析选项。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<IReadOnlyList<CodeReference>> FindMatchingFilesAsync(
        string description,
        ReferenceResolutionOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>构建引用索引。</summary>
    /// <param name="projectRoot">项目根路径。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<ReferenceIndex> BuildReferenceIndexAsync(
        string projectRoot,
        CancellationToken cancellationToken = default);
}
