namespace Tools.Handlers;

public sealed record ApplyPatchResult
{
    public required bool Success { get; init; }
    public required bool DryRun { get; init; }
    public required int FilesModified { get; init; }
    public required int FilesWouldModify { get; init; }
    public required int FilesFailed { get; init; }
    public required List<string> Details { get; init; } = [];
    public string? ErrorMessage { get; init; }

    public static ApplyPatchResult SuccessResult(int filesModified, List<string> details, bool dryRun) => new()
    {
        Success = true,
        DryRun = dryRun,
        FilesModified = dryRun ? 0 : filesModified,
        FilesWouldModify = dryRun ? filesModified : 0,
        FilesFailed = 0,
        Details = details,
    };

    public static ApplyPatchResult FailureResult(string errorMessage, List<string>? details = null) => new()
    {
        Success = false,
        DryRun = false,
        FilesModified = 0,
        FilesWouldModify = 0,
        FilesFailed = 1,
        Details = details ?? [],
        ErrorMessage = errorMessage,
    };

    public static ApplyPatchResult PartialResult(int filesModified, int filesFailed, List<string> details, bool dryRun) => new()
    {
        Success = false,
        DryRun = dryRun,
        FilesModified = dryRun ? 0 : filesModified,
        FilesWouldModify = dryRun ? filesModified : 0,
        FilesFailed = filesFailed,
        Details = details,
        ErrorMessage = $"Patch did not fully apply: {filesModified} file(s) modified, {filesFailed} file(s) failed",
    };
}
