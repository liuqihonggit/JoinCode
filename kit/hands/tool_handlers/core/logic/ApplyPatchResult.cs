namespace Tools.Handlers;

/// <summary>
/// ApplyPatch 操作结果,记录成功/失败统计、修改详情与已修改文件路径。
/// </summary>
public sealed record ApplyPatchResult
{
    /// <summary>
    /// 是否整体成功。
    /// </summary>
    public required bool Success { get; init; }
    /// <summary>
    /// 是否为 dry-run 模式(仅预演不实际写入)。
    /// </summary>
    public required bool DryRun { get; init; }
    /// <summary>
    /// 实际修改的文件数(dry-run 模式下为 0)。
    /// </summary>
    public required int FilesModified { get; init; }
    /// <summary>
    /// dry-run 模式下将会修改的文件数;非 dry-run 时为 0。
    /// </summary>
    public required int FilesWouldModify { get; init; }
    /// <summary>
    /// 应用失败的文件数。
    /// </summary>
    public required int FilesFailed { get; init; }
    /// <summary>
    /// 每个文件的处理详情文本列表。
    /// </summary>
    public required List<string> Details { get; init; } = [];
    /// <summary>
    /// 实际被修改的文件路径列表。
    /// </summary>
    public required List<string> ModifiedFilePaths { get; init; } = [];
    /// <summary>
    /// 失败时的错误消息;成功时为 null。
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// 构造整体成功的结果。
    /// </summary>
    /// <param name="filesModified">修改的文件数。</param>
    /// <param name="details">处理详情列表。</param>
    /// <param name="dryRun">是否 dry-run 模式。</param>
    /// <param name="modifiedPaths">被修改的文件路径列表,默认为空。</param>
    /// <returns>表示成功应用的 ApplyPatchResult 实例。</returns>
    public static ApplyPatchResult SuccessResult(int filesModified, List<string> details, bool dryRun, List<string>? modifiedPaths = null) => new()
    {
        Success = true,
        DryRun = dryRun,
        FilesModified = dryRun ? 0 : filesModified,
        FilesWouldModify = dryRun ? filesModified : 0,
        FilesFailed = 0,
        Details = details,
        ModifiedFilePaths = modifiedPaths ?? [],
    };

    /// <summary>
    /// 构造整体失败的结果。
    /// </summary>
    /// <param name="errorMessage">错误消息。</param>
    /// <param name="details">处理详情列表,默认为空。</param>
    /// <returns>表示失败应用的 ApplyPatchResult 实例。</returns>
    public static ApplyPatchResult FailureResult(string errorMessage, List<string>? details = null) => new()
    {
        Success = false,
        DryRun = false,
        FilesModified = 0,
        FilesWouldModify = 0,
        FilesFailed = 1,
        Details = details ?? [],
        ModifiedFilePaths = [],
        ErrorMessage = errorMessage,
    };

    /// <summary>
    /// 构造部分成功的结果(部分文件应用失败)。
    /// </summary>
    /// <param name="filesModified">成功修改的文件数。</param>
    /// <param name="filesFailed">应用失败的文件数。</param>
    /// <param name="details">处理详情列表。</param>
    /// <param name="dryRun">是否 dry-run 模式。</param>
    /// <param name="modifiedPaths">被修改的文件路径列表,默认为空。</param>
    /// <returns>表示部分应用的 ApplyPatchResult 实例。</returns>
    public static ApplyPatchResult PartialResult(int filesModified, int filesFailed, List<string> details, bool dryRun, List<string>? modifiedPaths = null) => new()
    {
        Success = false,
        DryRun = dryRun,
        FilesModified = dryRun ? 0 : filesModified,
        FilesWouldModify = dryRun ? filesModified : 0,
        FilesFailed = filesFailed,
        Details = details,
        ModifiedFilePaths = modifiedPaths ?? [],
        ErrorMessage = $"Patch did not fully apply: {filesModified} file(s) modified, {filesFailed} file(s) failed",
    };
}
