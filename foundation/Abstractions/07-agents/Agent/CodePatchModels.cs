namespace JoinCode.Abstractions.Interfaces.Doctor;

/// <summary>
/// 源码上下文 — 供 LLM 生成 patch 时参考
/// </summary>
public sealed record SourceCodeContext
{
    /// <summary>目标文件路径</summary>
    public required string FilePath { get; init; }

    /// <summary>当前文件内容</summary>
    public required string CurrentContent { get; init; }

    /// <summary>相关文件上下文（imports、接口定义等）</summary>
    public IReadOnlyList<SourceFileSnippet> RelatedSnippets { get; init; } = [];

    /// <summary>编译错误信息（如果有）</summary>
    public string? BuildErrorOutput { get; init; }
}

/// <summary>
/// 源码文件片段
/// </summary>
public sealed record SourceFileSnippet
{
    /// <summary>文件路径</summary>
    public required string FilePath { get; init; }

    /// <summary>内容</summary>
    public required string Content { get; init; }
}

/// <summary>
/// LLM 生成的源码 patch
/// </summary>
public sealed record CodePatch
{
    /// <summary>目标文件路径</summary>
    public required string TargetFilePath { get; init; }

    /// <summary>修改后的完整文件内容</summary>
    public required string PatchedContent { get; init; }

    /// <summary>修改说明</summary>
    public required string Description { get; init; }

    /// <summary>置信度 0-1</summary>
    public double Confidence { get; init; }

    /// <summary>LLM 的推理过程</summary>
    public string? Reasoning { get; init; }
}
