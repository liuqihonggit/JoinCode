namespace JoinCode.Abstractions.Interfaces;

public sealed class BriefSendResult {
    /// <summary>获取文件路径。</summary>
    public required string FilePath { get; init; }
    /// <summary>获取一个值，指示附件是否有效。</summary>
    public required bool IsValid { get; init; }
    /// <summary>获取文件大小（字节）。</summary>
    public long FileSize { get; init; }
    /// <summary>获取文件类型。</summary>
    public string? FileType { get; init; }
    /// <summary>获取错误信息。</summary>
    public string? ErrorMessage { get; init; }
}

public interface IBriefService {
    /// <summary>验证附件是否有效。</summary>
    BriefSendResult ValidateAttachment(string filePath, long? maxSizeBytes = null);

    /// <summary>格式化消息（含附件结果）。</summary>
    string FormatMessage(string message, IReadOnlyList<BriefSendResult>? attachments = null, bool isProactive = false);

    /// <summary>格式化消息（含附件路径）。</summary>
    string FormatMessageWithPaths(string message, IReadOnlyList<string>? attachmentPaths = null, bool isProactive = false);
}