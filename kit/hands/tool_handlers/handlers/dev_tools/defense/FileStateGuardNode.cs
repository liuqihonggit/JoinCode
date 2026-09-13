namespace Tools.Handlers;

/// <summary>
/// 文件状态守卫 node — 独立公共对象，包装 <see cref="IFileStateCache"/>，提供读前校验与脏写保护。
/// 文件写入/编辑工具注入此 node 确保先读后写、检测外部修改。
/// 对齐 TS: FileWriteTool.ts L198/L212 / FileEditTool.ts L275/L290。
/// </summary>
[Register(typeof(FileStateGuardNode), ServiceLifetime.Singleton)]
public sealed class FileStateGuardNode
{
    private readonly IFileStateCache? _fileStateCache;
    private readonly IFileSystem _fs;
    private readonly ILogger<FileStateGuardNode>? _logger;

    public FileStateGuardNode(
        IFileSystem fs,
        IFileStateCache? fileStateCache = null,
        ILogger<FileStateGuardNode>? logger = null)
    {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _fileStateCache = fileStateCache;
        _logger = logger;
    }

    /// <summary>
    /// 写前读校验 — 已存在的文件必须先读取才能写入/编辑。
    /// CLI 无状态模式（mcp_call 单次调用）下 FileStateCache 永远为空，跳过校验。
    /// </summary>
    /// <returns>true 表示已读或不需要校验，false 表示未读</returns>
    public bool HasBeenRead(string filePath)
    {
        if (_fileStateCache is null || !_fs.FileExists(filePath) || TestEnvironmentDetector.ForceNonInteractive)
            return true;

        return _fileStateCache.HasBeenRead(filePath);
    }

    /// <summary>
    /// 脏写保护（Stale-write guard）— 文件读后被外部修改则拒绝写入。
    /// Windows 时间戳误报回退：用检测编码读取文件比对内容，内容不变则放行。
    /// </summary>
    /// <returns>null 表示安全，(lastWriteMs, readTimestampMs) 表示检测到外部修改</returns>
    public async ValueTask<(long LastWriteMs, long ReadTimestampMs)?> CheckStaleWriteAsync(string filePath, CancellationToken ct)
    {
        if (_fileStateCache is null || !_fs.FileExists(filePath) || TestEnvironmentDetector.ForceNonInteractive)
            return null;

        var readTimestamp = _fileStateCache.GetReadTimestampMs(filePath);
        if (!readTimestamp.HasValue)
            return null;

        var lastWriteMs = new DateTimeOffset(_fs.GetLastWriteTimeUtc(filePath)).ToUnixTimeMilliseconds();
        if (lastWriteMs <= readTimestamp.Value + 1000) // 1s 容忍
            return null;

        // 时间戳显示已修改，但 Windows 上云同步/杀毒等会改时间戳而不改内容，比对内容兜底
        var readContent = _fileStateCache.GetReadContent(filePath);
        if (readContent is not null)
        {
            // 对齐 TS: 用检测到的编码读取文件，避免 UTF-16LE 内容比对错误
            var detectedEncoding = await FileEncodingDetector.DetectFromFileAsync(filePath, _fs, ct).ConfigureAwait(false);
            var currentContent = await _fs.ReadAllTextAsync(filePath, detectedEncoding, ct).ConfigureAwait(false);
            if (currentContent == readContent)
                return null; // 内容未变，安全放行
        }

        return (lastWriteMs, readTimestamp.Value);
    }
}
