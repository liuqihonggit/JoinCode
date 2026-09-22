namespace Infrastructure.IO;

/// <summary>
/// 工具结果文件服务 — 将超大工具调用结果持久化到磁盘,避免上下文膨胀
/// <para>对齐 TS 实现: 使用 FileMode.CreateNew 排他创建消除 TOCTOU 竞态</para>
/// <para>预览在换行符处截断,保留可读结构</para>
/// </summary>
[Register(typeof(JoinCode.Abstractions.LLM.Chat.IToolResultFileService), ServiceLifetime.Singleton)]
public sealed partial class ToolResultFileService : ServiceEntity, JoinCode.Abstractions.LLM.Chat.IToolResultFileService {
    private readonly ILogger<ToolResultFileService>? _logger;
    private readonly IFileSystem _fs;
    private readonly string _baseDir;

    /// <summary>
    /// 构造工具结果文件服务
    /// </summary>
    /// <param name="fs">文件系统抽象</param>
    /// <param name="logger">可选日志记录器</param>
    public ToolResultFileService(IFileSystem fs, ILogger<ToolResultFileService>? logger = null) {
        _fs = fs;
        _logger = logger;
        _baseDir = Path.Combine(
            AppDataConstants.AppDataFolder,
            AppDataConstants.ToolResultsFolderName);
    }

    /// <summary>
    /// 同步持久化工具结果 — 使用排他创建,已存在则跳过
    /// </summary>
    /// <param name="sessionId">会话标识,作为子目录名</param>
    /// <param name="toolUseId">工具调用标识,作为文件名</param>
    /// <param name="content">工具结果内容</param>
    /// <returns>持久化结果,包含文件路径、原始大小、预览与是否截断标志</returns>
    public async ValueTask<JoinCode.Abstractions.LLM.Chat.PersistedToolResult> PersistToolResult(string sessionId, string toolUseId, string content) {
        var dir = Path.Combine(_baseDir, sessionId);
        _fs.CreateDirectory(dir);

        var filename = SanitizeFilename(toolUseId) + ".txt";
        var filepath = Path.Combine(dir, filename);

        // 对齐 TS: 使用 'wx' 标志（排他创建），已存在则跳过
        // TS: writeFile(filepath, contentStr, { flag: 'wx' }) — 原子性排他创建
        // C#: 使用 FileMode.CreateNew 替代 File.Exists + File.WriteAllText（消除 TOCTOU 竞态）
        try {
            await using var stream = _fs.CreateStream(filepath, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite);
            await using var writer = new StreamWriter(stream);
            writer.Write(content);
        } catch (IOException ex) when (_fs.FileExists(filepath)) {
            // 已存在 — 对齐 TS EEXIST 处理：跳过写入
            _logger?.LogDebug(ex, "Tool result file already exists (created by another process), skipping: {Filepath}", filepath);
        }

        // 对齐 TS generatePreview: 在换行符处截断预览
        var (preview, hasMore) = GeneratePreview(content, JoinCode.Abstractions.LLM.Chat.ContentReplacementConstants.PreviewSizeChars);

        _logger?.LogDebug("Persisted tool result to {Filepath}, size={Size}", filepath, content.Length);

        return new JoinCode.Abstractions.LLM.Chat.PersistedToolResult {
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
    /// <param name="sessionId">会话标识,作为子目录名</param>
    /// <param name="toolUseId">工具调用标识,作为文件名</param>
    /// <param name="content">工具结果内容</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>持久化结果,包含文件路径、原始大小、预览与是否截断标志</returns>
    public async Task<JoinCode.Abstractions.LLM.Chat.PersistedToolResult> PersistToolResultAsync(
        string sessionId, string toolUseId, string content, CancellationToken cancellationToken = default) {
        var dir = Path.Combine(_baseDir, sessionId);
        _fs.CreateDirectory(dir);

        var filename = SanitizeFilename(toolUseId) + ".txt";
        var filepath = Path.Combine(dir, filename);

        try {
            await using var stream = _fs.CreateStream(filepath, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite);
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(content.AsMemory(), cancellationToken).ConfigureAwait(false);
        } catch (IOException ex) when (_fs.FileExists(filepath)) {
            // 已存在 — 对齐 TS EEXIST 处理：跳过写入
            _logger?.LogDebug(ex, "ToolResultFileService: file already exists (async), skipping: {Filepath}", filepath);
        }

        var (preview, hasMore) = GeneratePreview(content, JoinCode.Abstractions.LLM.Chat.ContentReplacementConstants.PreviewSizeChars);

        _logger?.LogDebug("Persisted tool result to {Filepath}, size={Size}", filepath, content.Length);

        return new JoinCode.Abstractions.LLM.Chat.PersistedToolResult {
            Filepath = filepath,
            OriginalSize = content.Length,
            IsJson = content.TrimStart().StartsWith('{') || content.TrimStart().StartsWith('['),
            Preview = preview,
            HasMore = hasMore
        };
    }

    /// <summary>
    /// 读取已持久化的工具结果
    /// </summary>
    /// <param name="sessionId">会话标识</param>
    /// <param name="toolUseId">工具调用标识</param>
    /// <returns>工具结果内容;文件不存在或读取失败返回 null</returns>
    public async ValueTask<string?> ReadToolResult(string sessionId, string toolUseId) {
        var dir = Path.Combine(_baseDir, sessionId);
        var filename = SanitizeFilename(toolUseId) + ".txt";
        var filepath = Path.Combine(dir, filename);

        if (!_fs.FileExists(filepath))
            return null;

        try {
            return await _fs.ReadAllText(filepath).ConfigureAwait(false);
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "Failed to read persisted tool result from {Filepath}", filepath);
            return null;
        }
    }

    private static string SanitizeFilename(string id) {
        var chars = id.ToCharArray();
        for (var i = 0; i < chars.Length; i++) {
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
    private static (string Preview, bool HasMore) GeneratePreview(string content, int maxChars) {
        if (content.Length <= maxChars)
            return (content, false);

        var truncated = content.AsSpan(0, maxChars);
        var lastNewline = truncated.LastIndexOf('\n');
        var cutPoint = lastNewline > maxChars / 2 ? lastNewline : maxChars;

        return (content.AsSpan(0, cutPoint).ToString(), true);
    }
}