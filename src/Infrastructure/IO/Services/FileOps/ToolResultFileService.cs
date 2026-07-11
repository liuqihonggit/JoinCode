namespace Infrastructure.IO;

[Register(typeof(JoinCode.Abstractions.LLM.Chat.IToolResultFileService))]
public sealed partial class ToolResultFileService : JoinCode.Abstractions.LLM.Chat.IToolResultFileService
{
    [Inject] private readonly ILogger<ToolResultFileService>? _logger;
    private readonly IFileSystem _fs;
    private readonly string _baseDir;

    public ToolResultFileService(IFileSystem fs, ILogger<ToolResultFileService>? logger = null)
    {
        _fs = fs;
        _logger = logger;
        _baseDir = Path.Combine(
            AppDataConstants.AppDataFolder,
            AppDataConstants.ToolResultsFolderName);
    }

    public JoinCode.Abstractions.LLM.Chat.PersistedToolResult PersistToolResult(string sessionId, string toolUseId, string content)
    {
        var dir = Path.Combine(_baseDir, sessionId);
        _fs.CreateDirectory(dir);

        var filename = SanitizeFilename(toolUseId) + ".txt";
        var filepath = Path.Combine(dir, filename);

        // 对齐 TS: 使用 'wx' 标志（排他创建），已存在则跳过
        // TS: writeFile(filepath, contentStr, { flag: 'wx' }) — 原子性排他创建
        // C#: 使用 FileMode.CreateNew 替代 File.Exists + File.WriteAllText（消除 TOCTOU 竞态）
        try
        {
            using var stream = _fs.CreateStream(filepath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using var writer = new StreamWriter(stream);
            writer.Write(content);
        }
        catch (IOException ex) when (_fs.FileExists(filepath))
        {
            // 已存在 — 对齐 TS EEXIST 处理：跳过写入
            _logger?.LogDebug(ex, "Tool result file already exists (created by another process), skipping: {Filepath}", filepath);
        }

        // 对齐 TS generatePreview: 在换行符处截断预览
        var (preview, hasMore) = GeneratePreview(content, JoinCode.Abstractions.LLM.Chat.ContentReplacementConstants.PreviewSizeChars);

        _logger?.LogDebug("Persisted tool result to {Filepath}, size={Size}", filepath, content.Length);

        return new JoinCode.Abstractions.LLM.Chat.PersistedToolResult
        {
            Filepath = filepath,
            OriginalSize = content.Length,
            IsJson = content.TrimStart().StartsWith('{') || content.TrimStart().StartsWith('['),
            Preview = preview,
            HasMore = hasMore
        };
    }

    /// <summary>
    /// 异步持久化工具结果 — 对齐 TS Promise.all 并发持久化
    /// </summary>
    public async Task<JoinCode.Abstractions.LLM.Chat.PersistedToolResult> PersistToolResultAsync(
        string sessionId, string toolUseId, string content, CancellationToken cancellationToken = default)
    {
        var dir = Path.Combine(_baseDir, sessionId);
        _fs.CreateDirectory(dir);

        var filename = SanitizeFilename(toolUseId) + ".txt";
        var filepath = Path.Combine(dir, filename);

        try
        {
            using var stream = _fs.CreateStream(filepath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using var writer = new StreamWriter(stream);
            await writer.WriteAsync(content.AsMemory(), cancellationToken).ConfigureAwait(false);
        }
        catch (IOException ex) when (_fs.FileExists(filepath))
        {
            // 已存在 — 对齐 TS EEXIST 处理：跳过写入
            _logger?.LogDebug(ex, "ToolResultFileService: file already exists (async), skipping: {Filepath}", filepath);
        }

        var (preview, hasMore) = GeneratePreview(content, JoinCode.Abstractions.LLM.Chat.ContentReplacementConstants.PreviewSizeChars);

        _logger?.LogDebug("Persisted tool result to {Filepath}, size={Size}", filepath, content.Length);

        return new JoinCode.Abstractions.LLM.Chat.PersistedToolResult
        {
            Filepath = filepath,
            OriginalSize = content.Length,
            IsJson = content.TrimStart().StartsWith('{') || content.TrimStart().StartsWith('['),
            Preview = preview,
            HasMore = hasMore
        };
    }

    public string? ReadToolResult(string sessionId, string toolUseId)
    {
        var dir = Path.Combine(_baseDir, sessionId);
        var filename = SanitizeFilename(toolUseId) + ".txt";
        var filepath = Path.Combine(dir, filename);

        if (!_fs.FileExists(filepath))
            return null;

        try
        {
            return _fs.ReadAllText(filepath);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to read persisted tool result from {Filepath}", filepath);
            return null;
        }
    }

    private static string SanitizeFilename(string id)
    {
        var chars = id.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            var c = chars[i];
            if (char.IsLetterOrDigit(c) || c == '-' || c == '_')
                continue;
            chars[i] = '_';
        }
        return new string(chars);
    }

    /// <summary>
    /// 对齐 TS generatePreview: 在换行符处截断预览
    /// </summary>
    private static (string Preview, bool HasMore) GeneratePreview(string content, int maxChars)
    {
        if (content.Length <= maxChars)
            return (content, false);

        var truncated = content.AsSpan(0, maxChars);
        var lastNewline = truncated.LastIndexOf('\n');
        var cutPoint = lastNewline > maxChars / 2 ? lastNewline : maxChars;

        return (content.AsSpan(0, cutPoint).ToString(), true);
    }
}
