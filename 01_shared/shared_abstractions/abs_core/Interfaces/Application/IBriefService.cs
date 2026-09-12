namespace JoinCode.Abstractions.Interfaces;

public sealed class BriefSendResult
{
    public required string FilePath { get; init; }
    public required bool IsValid { get; init; }
    public long FileSize { get; init; }
    public string? FileType { get; init; }
    public string? ErrorMessage { get; init; }
}

public interface IBriefService
{
    BriefSendResult ValidateAttachment(string filePath, long? maxSizeBytes = null);

    string FormatMessage(string message, IReadOnlyList<BriefSendResult>? attachments = null, bool isProactive = false);

    string FormatMessageWithPaths(string message, IReadOnlyList<string>? attachmentPaths = null, bool isProactive = false);
}