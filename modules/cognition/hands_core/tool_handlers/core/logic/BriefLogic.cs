namespace Tools.Handlers;

/// <summary>
/// 消息简报逻辑,负责附件校验与消息格式化。
/// </summary>
[Register(typeof(IBriefService), ServiceLifetime.Singleton)]
public sealed partial class BriefLogic : ServiceEntity, IBriefService {

    /// <summary>
    /// 初始化 BriefLogic 的新实例。
    /// </summary>
    /// <param name="fs">文件系统抽象。</param>
    public BriefLogic(IFileSystem fs) {
        _fs = fs;
    }
    private const long DefaultMaxAttachmentSize = 10 * 1024 * 1024;
    private readonly IFileSystem _fs;

    /// <summary>
    /// 校验附件文件路径与大小是否满足发送约束。
    /// </summary>
    /// <param name="filePath">附件文件路径。</param>
    /// <param name="maxSizeBytes">最大允许字节数,默认 10MB。</param>
    /// <returns>包含有效性、文件大小、类型与错误信息的校验结果。</returns>
    public BriefSendResult ValidateAttachment(string filePath, long? maxSizeBytes = null) {
        if (string.IsNullOrEmpty(filePath))
            return new BriefSendResult {
                FilePath = filePath ?? string.Empty,
                IsValid = false,
                ErrorMessage = L.T(StringKey.BriefFilePathEmpty)
            };

        if (!_fs.FileExists(filePath))
            return new BriefSendResult {
                FilePath = filePath,
                IsValid = false,
                ErrorMessage = L.T(StringKey.BriefFileNotExist)
            };

        var sizeLimit = maxSizeBytes ?? DefaultMaxAttachmentSize;
        long fileSize;
        using (var sizeStream = _fs.OpenRead(filePath)) {
            fileSize = sizeStream.Length;
        }

        if (fileSize > sizeLimit)
            return new BriefSendResult {
                FilePath = filePath,
                IsValid = false,
                FileSize = fileSize,
                ErrorMessage = L.T(StringKey.BriefFileSizeExceeded)
            };

        return new BriefSendResult {
            FilePath = filePath,
            IsValid = true,
            FileSize = fileSize,
            FileType = Path.GetExtension(filePath).ToLowerInvariant()
        };
    }

    /// <summary>
    /// 格式化消息文本,可选附带附件清单与主动推送标签。
    /// </summary>
    /// <param name="message">消息正文。</param>
    /// <param name="attachments">附件校验结果列表,仅展示有效附件。</param>
    /// <param name="isProactive">是否为主动推送消息, true 时添加主动标签。</param>
    /// <returns>格式化后的消息字符串。</returns>
    public string FormatMessage(string message, IReadOnlyList<BriefSendResult>? attachments = null, bool isProactive = false) {
        var sb = new StringBuilder();

        if (isProactive)
            sb.AppendLine(L.T(StringKey.BriefProactiveLabel));

        if (!string.IsNullOrEmpty(message)) {
            sb.Append(message);
            sb.AppendLine();
        }

        if (attachments is { Count: > 0 }) {
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine(L.T(StringKey.BriefAttachmentLabel));
            foreach (var attachment in attachments) {
                if (attachment.IsValid) {
                    var fileName = Path.GetFileName(attachment.FilePath);
                    sb.AppendLine($"- `{fileName}` ({ContentReplacementConstants.FormatFileSize(attachment.FileSize)})");
                }
            }
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// 基于附件路径列表格式化消息,先逐路径校验附件再委托 FormatMessage。
    /// </summary>
    /// <param name="message">消息正文。</param>
    /// <param name="attachmentPaths">附件文件路径列表。</param>
    /// <param name="isProactive">是否为主动推送消息。</param>
    /// <returns>格式化后的消息字符串。</returns>
    public string FormatMessageWithPaths(string message, IReadOnlyList<string>? attachmentPaths = null, bool isProactive = false) {
        var attachments = attachmentPaths?.Select(p => ValidateAttachment(p)).ToList();
        return FormatMessage(message, attachments as IReadOnlyList<BriefSendResult>, isProactive);
    }
}