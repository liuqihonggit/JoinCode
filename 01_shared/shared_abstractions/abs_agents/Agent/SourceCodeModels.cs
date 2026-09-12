namespace JoinCode.Abstractions.Interfaces.Doctor;

/// <summary>
/// 源码仓库位置信息
/// </summary>
public sealed record SourceCodeLocation
{
    /// <summary>git 仓库根目录（包含 .git 的目录）</summary>
    public required string GitRoot { get; init; }

    /// <summary>源码是否可用（.git 存在且可读）</summary>
    public required bool IsAvailable { get; init; }

    /// <summary>当前分支</summary>
    public string? CurrentBranch { get; init; }

    /// <summary>当前 commit hash</summary>
    public string? CurrentCommitHash { get; init; }

    /// <summary>定位失败原因</summary>
    public string? FailureReason { get; init; }
}

/// <summary>
/// 全量编译结果（七层 slnx）
/// </summary>
public sealed record FullBuildResult
{
    /// <summary>是否全部成功</summary>
    public required bool Success { get; init; }

    /// <summary>每层 slnx 的编译结果</summary>
    public required IReadOnlyList<SlnxBuildResult> LayerResults { get; init; }

    /// <summary>编译产物 exe 路径</summary>
    public string? ArtifactExePath { get; init; }

    /// <summary>总耗时</summary>
    public TimeSpan TotalDuration { get; init; }

    /// <summary>首个失败的层</summary>
    public int? FirstFailedLayer => LayerResults.FirstOrDefault(r => !r.Success)?.Layer;
}

/// <summary>
/// 单层 slnx 编译结果
/// </summary>
public sealed record SlnxBuildResult
{
    /// <summary>层编号（1-7）</summary>
    public required int Layer { get; init; }

    /// <summary>slnx 文件名</summary>
    public required string SlnxName { get; init; }

    /// <summary>是否成功</summary>
    public required bool Success { get; init; }

    /// <summary>退出码</summary>
    public int ExitCode { get; init; }

    /// <summary>编译输出</summary>
    public string? Output { get; init; }

    /// <summary>耗时</summary>
    public TimeSpan Duration { get; init; }
}

/// <summary>
/// exe 替换结果
/// </summary>
public sealed record ExeSwapResult
{
    /// <summary>是否成功</summary>
    public required bool Success { get; init; }

    /// <summary>旧 exe 路径</summary>
    public string? OldExePath { get; init; }

    /// <summary>新 exe 路径</summary>
    public string? NewExePath { get; init; }

    /// <summary>结果描述</summary>
    public string? Description { get; init; }
}
