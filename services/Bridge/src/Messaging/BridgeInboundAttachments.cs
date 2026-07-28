
namespace Core.Bridge;

/// <summary>
/// 入站附件数据模型 — 对齐 TS 端 inboundAttachments.ts InboundAttachment
/// </summary>
public sealed class BridgeInboundAttachment
{
    [JsonPropertyName("file_uuid")]
    public required string FileUuid { get; init; }

    [JsonPropertyName("file_name")]
    public required string FileName { get; init; }
}

/// <summary>
/// 入站附件解析服务 — 对齐 TS 端 inboundAttachments.ts
/// 处理 Bridge 远程控制场景中 Web 编辑器上传的文件附件
/// best-effort 设计：任何失败只跳过该附件不阻塞消息
/// </summary>
public static class BridgeInboundAttachments
{
    /// <summary>
    /// 从消息提取 file_attachments — 对齐 TS 端 extractInboundAttachments
    /// </summary>
    public static List<BridgeInboundAttachment> ExtractInboundAttachments(JsonElement msg)
    {
        if (msg.ValueKind != JsonValueKind.Object) return [];

        if (!msg.TryGetProperty("file_attachments", out var attachments) || attachments.ValueKind != JsonValueKind.Array)
            return [];

        var result = new List<BridgeInboundAttachment>();
        foreach (var item in attachments.EnumerateArray())
        {
            try
            {
                if (item.ValueKind != JsonValueKind.Object) continue;

                var fileUuid = item.TryGetProperty("file_uuid", out var uuidProp) ? uuidProp.GetString() : null;
                var fileName = item.TryGetProperty("file_name", out var nameProp) ? nameProp.GetString() : null;

                if (fileUuid is not null && fileName is not null)
                {
                    result.Add(new BridgeInboundAttachment { FileUuid = fileUuid, FileName = fileName });
                }
            }
            catch (Exception ex)
            {
                // best-effort: 跳过无法解析的附件
                System.Diagnostics.Trace.WriteLine($"[BridgeInboundAttachments] Skip unparseable attachment: {ex.Message}");
            }
        }

        return result;
    }

    /// <summary>
    /// 并行下载附件到本地，返回 @"path" 引用前缀字符串 — 对齐 TS 端 resolveInboundAttachments
    /// 下载到 ~/.jcc/uploads/{sessionId}/
    /// </summary>
    public static async Task<string> ResolveInboundAttachmentsAsync(
        List<BridgeInboundAttachment> attachments,
        string sessionId,
        HttpClient httpClient,
        IFileSystem fs,
        CancellationToken ct)
    {
        if (attachments.Count == 0) return string.Empty;

        var uploadDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            AppDataConstants.AppDataFolder, "uploads", sessionId);

        fs.CreateDirectory(uploadDir);

        var pathRefs = new List<string>();

        // 并行下载所有附件
        var tasks = attachments.Select(async attachment =>
        {
            try
            {
                var filePath = Path.Combine(uploadDir, SanitizeFileName(attachment.FileName));

                // 通过 OAuth 认证的 API 下载文件
                var url = $"/api/oauth/files/{attachment.FileUuid}/content";
                var response = await httpClient.GetAsync(url, ct).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                await using var fs2 = fs.CreateStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
                await response.Content.CopyToAsync(fs2, ct).ConfigureAwait(false);

                return $@"@""{filePath}""";
            }
            catch
            {
                // best-effort: 跳过下载失败的附件
                return null;
            }
        });

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);

        foreach (var pathRef in results)
        {
            if (pathRef is not null)
            {
                pathRefs.Add(pathRef);
            }
        }

        return pathRefs.Count > 0 ? string.Join("\n", pathRefs) + "\n" : string.Empty;
    }

    /// <summary>
    /// 将路径引用前缀插入内容的最后一个文本块 — 对齐 TS 端 prependPathRefs
    /// TS 端插入到最后一个文本块（因为 processUserInputBase 从最后一个文本块读取输入）
    /// </summary>
    public static string PrependPathRefs(string content, string prefix)
    {
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
        CancellationToken ct)
    {
        var attachments = ExtractInboundAttachments(msg);
        if (attachments.Count == 0) return content;

        var prefix = await ResolveInboundAttachmentsAsync(attachments, sessionId, httpClient, fs, ct).ConfigureAwait(false);
        return PrependPathRefs(content, prefix);
    }

    /// <summary>
    /// 清理文件名中的非法字符
    /// </summary>
    private static string SanitizeFileName(string fileName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var result = new char[fileName.Length];
        var len = 0;
        foreach (var c in fileName)
        {
            if (Array.IndexOf(invalid, c) < 0)
            {
                result[len++] = c;
            }
        }

        return len == 0 ? "unnamed" : new string(result, 0, len);
    }
}
