namespace Tools.Handlers;

[Register]
public sealed partial class BriefLogic : IBriefService
{
    private const long DefaultMaxAttachmentSize = 10 * 1024 * 1024;
    [Inject] private readonly IFileSystem _fs;

    public BriefSendResult ValidateAttachment(string filePath, long? maxSizeBytes = null)
    {
        if (string.IsNullOrEmpty(filePath))
            return new BriefSendResult
            {
                FilePath = filePath ?? string.Empty,
                IsValid = false,
                ErrorMessage = L.T(StringKey.BriefFilePathEmpty)
            };

        if (!_fs.FileExists(filePath))
            return new BriefSendResult
            {
                FilePath = filePath,
                IsValid = false,
                ErrorMessage = L.T(StringKey.BriefFileNotExist)
            };

        var sizeLimit = maxSizeBytes ?? DefaultMaxAttachmentSize;
        long fileSize;
        using (var sizeStream = _fs.OpenRead(filePath))
        {
            fileSize = sizeStream.Length;
        }

        if (fileSize > sizeLimit)
            return new BriefSendResult
            {
                FilePath = filePath,
                IsValid = false,
                FileSize = fileSize,
                ErrorMessage = L.T(StringKey.BriefFileSizeExceeded)
            };

        return new BriefSendResult
        {
            FilePath = filePath,
            IsValid = true,
            FileSize = fileSize,
            FileType = Path.GetExtension(filePath).ToLowerInvariant()
        };
    }

    public string FormatMessage(string message, IReadOnlyList<BriefSendResult>? attachments = null, bool isProactive = false)
    {
        var sb = new StringBuilder();

        if (isProactive)
            sb.AppendLine(L.T(StringKey.BriefProactiveLabel));

        if (!string.IsNullOrEmpty(message))
        {
            sb.Append(message);
            sb.AppendLine();
        }

        if (attachments is { Count: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine(L.T(StringKey.BriefAttachmentLabel));
            foreach (var attachment in attachments)
            {
                if (attachment.IsValid)
                {
                    var fileName = Path.GetFileName(attachment.FilePath);
                    sb.AppendLine($"- `{fileName}` ({ContentReplacementConstants.FormatFileSize(attachment.FileSize)})");
                }
            }
        }

        return sb.ToString().TrimEnd();
    }

    public string FormatMessageWithPaths(string message, IReadOnlyList<string>? attachmentPaths = null, bool isProactive = false)
    {
        var attachments = attachmentPaths?.Select(p => ValidateAttachment(p)).ToList();
        return FormatMessage(message, attachments as IReadOnlyList<BriefSendResult>, isProactive);
    }
}