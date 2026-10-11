
// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
#pragma warning disable JCC11003
namespace Core.Bridge;

/// <summary>
/// 入站附件数据模型 — 对齐 TS 端 inboundAttachments.ts InboundAttachment
/// </summary>
public sealed class BridgeInboundAttachment {
    /// <summary>文件 UUID — 服务端附件标识</summary>
    [JsonPropertyName("file_uuid")]
    public required string FileUuid { get; init; }

    /// <summary>文件名 — 上传时的原始文件名</summary>
    [JsonPropertyName("file_name")]
    public required string FileName { get; init; }
}

/// <summary>
/// 入站附件消息包装 — 对齐 TS 端 inboundAttachments.ts extractInboundAttachments 的消息体
/// 用 List&lt;JsonElement&gt; 承载以保持 best-effort 语义：单个附件反序列化失败只跳过该附件
/// </summary>
public sealed class BridgeInboundAttachmentsMessage {
    /// <summary>文件附件列表（JsonElement 延迟解析，逐个反序列化以隔离失败）</summary>
    [JsonPropertyName("file_attachments")]
    public List<JsonElement> FileAttachments { get; init; } = [];
}

/// <summary>
/// 入站附件解析服务 — 对齐 TS 端 inboundAttachments.ts
/// 处理 Bridge 远程控制场景中 Web 编辑器上传的文件附件
/// best-effort 设计：任何失败只跳过该附件不阻塞消息
/// </summary>
public static class BridgeInboundAttachments {
    /// <summary>
    /// 从消息提取 file_attachments — 对齐 TS 端 extractInboundAttachments
    /// </summary>
    public static List<BridgeInboundAttachment> ExtractInboundAttachments(JsonElement msg, ILogger? logger = null) {
        if (msg.ValueKind != JsonValueKind.Object) return [];

        BridgeInboundAttachmentsMessage? wrapper;
        try {
            wrapper = msg.Deserialize(BridgeJsonContext.Default.BridgeInboundAttachmentsMessage);
        } catch (JsonException ex) {
            // best-effort: 消息体无法解析，跳过全部附件
            logger?.LogWarning(ex, "[BridgeInboundAttachments] Skip unparseable attachments");
            return [];
        }

        if (wrapper?.FileAttachments is null) return [];

        var result = new List<BridgeInboundAttachment>();
        foreach (var item in wrapper.FileAttachments) {
            try {
                if (item.ValueKind != JsonValueKind.Object) continue;

                var attachment = item.Deserialize(BridgeJsonContext.Default.BridgeInboundAttachment);
                if (attachment is not null) {
                    result.Add(attachment);
                }
            } catch (Exception ex) {
                // best-effort: 跳过无法解析的附件
                logger?.LogWarning(ex, "[BridgeInboundAttachments] Skip unparseable attachment");
            }
        }

        return result;
    }

    /// <summary>
    /// 并行下载附件到本地，返回 @"path" 引用前缀字符串 — 对齐 TS 端 resolveInboundAttachments
    /// 下载到 ~/.jcc/uploads/{sessionId}/,复用 IBatchDownloader 多线程分片并行
    /// </summary>
    public static async Task<string> ResolveInboundAttachmentsAsync(
        List<BridgeInboundAttachment> attachments,
        string sessionId,
        HttpClient httpClient,
        IFileSystem fs,
        IBatchDownloader batchDownloader,
        CancellationToken ct) {
        if (attachments.Count == 0) return string.Empty;

        var uploadDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            AppDataConstants.AppDataFolder, "uploads", sessionId);

        fs.CreateDirectory(uploadDir);

        var headers = new Dictionary<string, string>();
        foreach (var header in httpClient.DefaultRequestHeaders) {
            headers[header.Key] = string.Join(",", header.Value);
        }

        var baseUrl = httpClient.BaseAddress!;
        var items = attachments.ConvertAll(a => new BatchDownloadItem(
            new Uri(baseUrl, $"/api/oauth/files/{a.FileUuid}/content").ToString(),
            Path.Combine(uploadDir, SanitizeFileName(a.FileName)),
            new DownloadOptions { MaxThreads = 4, Resume = false, Headers = headers }));

        var results = await batchDownloader.DownloadAllAsync(items, 4, ct).ConfigureAwait(false);

        var pathRefs = new List<string>();
        foreach (var r in results) {
            if (r.Success) pathRefs.Add($@"@""{r.FilePath}""");
        }

        return pathRefs.Count > 0 ? string.Join("\n", pathRefs) + "\n" : string.Empty;
    }

    /// <summary>
    /// 将路径引用前缀插入内容的最后一个文本块 — 对齐 TS 端 prependPathRefs
    /// TS 端插入到最后一个文本块（因为 processUserInputBase 从最后一个文本块读取输入）
    /// </summary>
    public static string PrependPathRefs(string content, string prefix) {
        if (string.IsNullOrEmpty(prefix)) return content;
        if (string.IsNullOrEmpty(content)) return prefix;

        return prefix + content;
    }

    /// <summary>
    /// 便捷: 提取+下载+前缀插入一步到位 — 对齐 TS 端 resolveAndPrepend
    /// </summary>
    public static async Task<string> ResolveAndPrependAsync(
        JsonElement msg,
        string content,
        string sessionId,
        HttpClient httpClient,
        IFileSystem fs,
        IBatchDownloader batchDownloader,
        CancellationToken ct) {
        var attachments = ExtractInboundAttachments(msg);
        if (attachments.Count == 0) return content;

        var prefix = await ResolveInboundAttachmentsAsync(attachments, sessionId, httpClient, fs, batchDownloader, ct).ConfigureAwait(false);
        return PrependPathRefs(content, prefix);
    }

    /// <summary>文件名非法字符集（缓存避免每次调用 Path.GetInvalidFileNameChars + Array.IndexOf 的 O(n²) 开销）</summary>
    private static readonly FrozenSet<char> InvalidFileNameChars = FrozenSet.Create(Path.GetInvalidFileNameChars());

    /// <summary>
    /// 清理文件名中的非法字符
    /// </summary>
    private static string SanitizeFileName(string fileName) {
        var result = new char[fileName.Length];
        var len = 0;
        foreach (var c in fileName) {
            if (!InvalidFileNameChars.Contains(c)) {
                result[len++] = c;
            }
        }

        return len == 0 ? "unnamed" : new string(result, 0, len);
    }
}