namespace Infrastructure.IO;

/// <summary>
/// MCP 二进制内容持久化服务 — 对齐 TS mcpOutputStorage.ts
/// 将 MCP 工具返回的二进制内容（音频、PDF 等）写入磁盘，返回文件路径
/// 图片类型走 base64 内联路径（ImageBlock），不经过此服务
/// 静态辅助方法见 JoinCode.Abstractions.LLM.Chat.McpBinaryHelper
/// </summary>
[Register(typeof(JoinCode.Abstractions.LLM.Chat.IMcpOutputStorage))]
public sealed partial class McpOutputStorage : JoinCode.Abstractions.LLM.Chat.IMcpOutputStorage
{
    [Inject] private readonly ILogger<McpOutputStorage>? _logger;
    private readonly IFileSystem _fs;
    private readonly string _baseDir;

    public McpOutputStorage(IFileSystem fs, ILogger<McpOutputStorage>? logger = null)
    {
        _fs = fs;
        _logger = logger;
        _baseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "jcc",
            "mcp-output");
    }

    public JoinCode.Abstractions.LLM.Chat.PersistBinaryResult? PersistBinaryContent(ReadOnlySpan<byte> bytes, string? mimeType, string persistId)
    {
        var ext = ExtensionForMimeType(mimeType);
        var dir = _baseDir;
        _fs.CreateDirectory(dir);

        var filename = $"{SanitizePersistId(persistId)}.{ext}";
        var filepath = Path.Combine(dir, filename);

        try
        {
            _fs.WriteAllBytes(filepath, bytes.ToArray());
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to persist binary content to {Filepath}", filepath);
            return null;
        }

        _logger?.LogDebug("Persisted binary content to {Filepath}, size={Size}, ext={Ext}", filepath, bytes.Length, ext);

        return new JoinCode.Abstractions.LLM.Chat.PersistBinaryResult
        {
            Filepath = filepath,
            Size = bytes.Length,
            Ext = ext
        };
    }

    /// <summary>
    /// MIME 类型到扩展名映射 — 对齐 TS extensionForMimeType
    /// </summary>
    private static string ExtensionForMimeType(string? mimeType)
    {
        if (string.IsNullOrEmpty(mimeType))
            return "bin";

        var mt = mimeType.AsSpan();
        var semiIndex = mt.IndexOf(';');
        if (semiIndex >= 0)
            mt = mt[..semiIndex];
        mt = mt.Trim();

        foreach (var (mime, ext) in MimeTypeExtensions)
        {
            if (mt.SequenceEqual(mime.Span))
                return ext;
        }

        // 未知类型用子类型作为扩展名
        var slashIndex = mt.IndexOf('/');
        if (slashIndex >= 0 && slashIndex < mt.Length - 1)
        {
            var subType = mt[(slashIndex + 1)..];
            var extChars = new char[subType.Length];
            subType.CopyTo(extChars);
            for (var i = 0; i < extChars.Length; i++)
            {
                if (extChars[i] == '+')
                    extChars[i] = '-';
            }
            return new string(extChars);
        }

        return "bin";
    }

    private static string SanitizePersistId(string id)
    {
        var chars = id.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            var c = chars[i];
            if (char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.')
                continue;
            chars[i] = '_';
        }
        return new string(chars);
    }

    /// <summary>
    /// MIME 类型到扩展名映射表 — 对齐 TS extensionForMimeType
    /// </summary>
    private static readonly (ReadOnlyMemory<char> mime, string ext)[] MimeTypeExtensions =
    [
        // 文档
        ("application/pdf".AsMemory(), "pdf"),
        ("application/json".AsMemory(), "json"),
        ("text/csv".AsMemory(), "csv"),
        ("text/plain".AsMemory(), "txt"),
        ("text/html".AsMemory(), "html"),
        ("text/markdown".AsMemory(), "md"),
        ("application/vnd.openxmlformats-officedocument.wordprocessingml.document".AsMemory(), "docx"),
        ("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet".AsMemory(), "xlsx"),
        ("application/vnd.openxmlformats-officedocument.presentationml.presentation".AsMemory(), "pptx"),
        ("application/msword".AsMemory(), "doc"),
        ("application/vnd.ms-excel".AsMemory(), "xls"),
        // 音频
        ("audio/mpeg".AsMemory(), "mp3"),
        ("audio/wav".AsMemory(), "wav"),
        ("audio/ogg".AsMemory(), "ogg"),
        // 视频
        ("video/mp4".AsMemory(), "mp4"),
        ("video/webm".AsMemory(), "webm"),
        // 图片
        ("image/png".AsMemory(), "png"),
        ("image/jpeg".AsMemory(), "jpg"),
        ("image/gif".AsMemory(), "gif"),
        ("image/webp".AsMemory(), "webp"),
        ("image/svg+xml".AsMemory(), "svg"),
        // 压缩
        ("application/zip".AsMemory(), "zip"),
        ("application/gzip".AsMemory(), "gz"),
    ];
}
