namespace Infrastructure.IO;

/// <summary>
/// MCP 二进制内容持久化服务 — 对齐 TS mcpOutputStorage.ts
/// 将 MCP 工具返回的二进制内容（音频、PDF 等）写入磁盘，返回文件路径
/// 图片类型走 base64 内联路径（ImageBlock），不经过此服务
/// 静态辅助方法见 JoinCode.Abstractions.LLM.Chat.McpBinaryHelper
/// </summary>
[Register(typeof(JoinCode.Abstractions.LLM.Chat.IMcpOutputStorage), ServiceLifetime.Singleton)]
public sealed partial class McpOutputStorage : ServiceEntity, JoinCode.Abstractions.LLM.Chat.IMcpOutputStorage {
    private readonly ILogger<McpOutputStorage>? _logger;
    private readonly IFileSystem _fs;
    private readonly string _baseDir;

    /// <summary>
    /// 构造 MCP 输出存储服务
    /// </summary>
    /// <param name="fs">文件系统抽象</param>
    /// <param name="logger">可选日志记录器</param>
    public McpOutputStorage(IFileSystem fs, ILogger<McpOutputStorage>? logger = null) {
        _fs = fs;
        _logger = logger;
        _baseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            AppDataConstants.AppDataFolder,
            "mcp-output");
    }

    /// <summary>
    /// 将二进制内容持久化到磁盘,返回包含路径/大小/扩展名的结果
    /// </summary>
    /// <param name="bytes">二进制内容</param>
    /// <param name="mimeType">可选 MIME 类型,用于推断扩展名</param>
    /// <param name="persistId">持久化标识,用于生成文件名</param>
    /// <returns>持久化成功时返回结果对象,失败时返回 null</returns>
    public async ValueTask<JoinCode.Abstractions.LLM.Chat.PersistBinaryResult?> PersistBinaryContent(byte[] bytes, string? mimeType, string persistId) {
        var ext = ExtensionForMimeType(mimeType);
        var dir = _baseDir;
        _fs.CreateDirectory(dir);

        var filename = $"{SanitizePersistId(persistId)}.{ext}";
        var filepath = Path.Combine(dir, filename);

        try {
            await _fs.WriteAllBytes(filepath, bytes).ConfigureAwait(false);
        } catch (Exception ex) {
            _logger?.LogError(ex, "Failed to persist binary content to {Filepath}", filepath);
            return null;
        }

        _logger?.LogDebug("Persisted binary content to {Filepath}, size={Size}, ext={Ext}", filepath, bytes.Length, ext);

        return new JoinCode.Abstractions.LLM.Chat.PersistBinaryResult {
            Filepath = filepath,
            Size = bytes.Length,
            Ext = ext
        };
    }

    /// <summary>
    /// MIME 类型到扩展名映射 — 委托单数据源 MimeExtensionCatalog，未知类型用子类型作为扩展名回退
    /// </summary>
    private static string ExtensionForMimeType(string? mimeType) {
        if (MimeExtensionCatalog.TryGetExtension(mimeType, out var ext))
            return ext;

        // 未知类型用子类型作为扩展名（如 application/x-custom → x-custom）
        if (string.IsNullOrEmpty(mimeType))
            return "bin";

        var mt = mimeType.AsSpan();
        var semiIndex = mt.IndexOf(';');
        if (semiIndex >= 0)
            mt = mt[..semiIndex];
        mt = mt.Trim();

        var slashIndex = mt.IndexOf('/');
        if (slashIndex >= 0 && slashIndex < mt.Length - 1) {
            var subType = mt[(slashIndex + 1)..];
            var extChars = new char[subType.Length];
            subType.CopyTo(extChars);
            for (var i = 0; i < extChars.Length; i++) {
                if (extChars[i] == '+')
                    extChars[i] = '-';
            }
            return new string(extChars);
        }

        return "bin";
    }

    private static string SanitizePersistId(string id) {
        var chars = id.ToCharArray();
        for (var i = 0; i < chars.Length; i++) {
            var c = chars[i];
            if (char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.')
                continue;
            chars[i] = '_';
        }
        return new string(chars);
    }

}
