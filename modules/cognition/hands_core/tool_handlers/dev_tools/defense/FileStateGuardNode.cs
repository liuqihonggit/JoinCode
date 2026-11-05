namespace Tools.Handlers;

/// <summary>
/// 脏写检测结果 — 区分"未读"（LRU 淘汰/无记录）与"脏写"（外部修改）两种拒绝原因。
/// 对齐 TS FileWriteTool.ts L282: 无读记录时防御性拒绝，不静默放行。
/// </summary>
/// <param name="LastWriteMs">文件最近写入时间戳（Unix 毫秒）；未读时为 0</param>
/// <param name="ReadTimestampMs">上次读取时间戳（Unix 毫秒）；未读时为 0</param>
/// <param name="IsNotRead">true 表示无读记录（防御性拒绝），false 表示检测到外部修改</param>
public sealed record StaleWriteDetection(long LastWriteMs, long ReadTimestampMs, bool IsNotRead = false);

/// <summary>
/// 文件状态守卫 node — 独立公共对象，包装 <see cref="IFileStateCache"/>，提供读前校验与脏写保护。
/// 文件写入/编辑工具注入此 node 确保先读后写、检测外部修改。
/// 对齐 TS: FileWriteTool.ts L198/L212 / FileEditTool.ts L275/L290。
/// </summary>
[Register(typeof(FileStateGuardNode), ServiceLifetime.Singleton)]
public sealed class FileStateGuardNode {
    private readonly IFileStateCache? _fileStateCache;
    private readonly IFileSystem _fs;
    private readonly ILogger<FileStateGuardNode>? _logger;

    /// <summary>
    /// 构造文件状态守卫 node
    /// </summary>
    /// <param name="fs">文件系统抽象</param>
    /// <param name="fileStateCache">可选的文件状态缓存</param>
    /// <param name="logger">可选日志记录器</param>
    public FileStateGuardNode(
        IFileSystem fs,
        IFileStateCache? fileStateCache = null,
        ILogger<FileStateGuardNode>? logger = null) {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _fileStateCache = fileStateCache;
        _logger = logger;
    }

    /// <summary>
    /// 写前读校验 — 已存在的文件必须先完整读取才能写入/编辑。
    /// 部分读（isPartialView）视为未读，对齐 TS FileWriteTool.ts L199 / FileEditTool.ts L276。
    /// CLI 无状态模式（mcp_call 单次调用）下 FileStateCache 永远为空，跳过校验。
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>true 表示已完整读取或不需要校验，false 表示未读或仅部分读</returns>
    public bool HasBeenRead(string filePath) {
        if (_fileStateCache is null || !_fs.FileExists(filePath) || TestEnvironmentDetector.ForceNonInteractive)
            return true;

        // 对齐 TS: 部分读（isPartialView）视为未读，Edit/Write 必须先完整 Read
        var state = _fileStateCache.GetReadState(filePath);
        return state is not null && !state.IsPartialView;
    }

    /// <summary>
    /// 脏写保护（Stale-write guard）— 文件读后被外部修改则拒绝写入。
    /// 部分读无法做内容兜底（缓存内容不是全量），直接拒绝，对齐 TS FileWriteTool.ts L286-289。
    /// 全量读时 Windows 时间戳误报回退：用检测编码读取文件比对内容，内容不变则放行。
    /// 无读记录（LRU 淘汰）防御性拒绝，对齐 TS FileWriteTool.ts L282。
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>null 表示安全；非 null 表示拒绝（IsNotRead=true 未读，false 脏写）</returns>
    public async ValueTask<StaleWriteDetection?> CheckStaleWriteAsync(string filePath, CancellationToken ct) {
        if (_fileStateCache is null || !_fs.FileExists(filePath) || TestEnvironmentDetector.ForceNonInteractive)
            return null;

        var readState = _fileStateCache.GetReadState(filePath);
        var lastWriteMs = new DateTimeOffset(_fs.GetLastWriteTimeUtc(filePath)).ToUnixTimeMilliseconds();

        // 对齐 TS FileWriteTool.ts L282: 无读记录（LRU 淘汰）防御性拒绝，不静默放行
        if (readState is null)
            return new StaleWriteDetection(lastWriteMs, 0, IsNotRead: true);

        if (lastWriteMs <= readState.TimestampMs + 1000) // 1s 容忍
            return null;

        // 对齐 TS FileWriteTool.ts L286-289: 部分读无法做内容兜底（缓存内容不是全量），直接拒绝
        if (readState.IsPartialView)
            return new StaleWriteDetection(lastWriteMs, readState.TimestampMs);

        // 时间戳显示已修改，但 Windows 上云同步/杀毒等会改时间戳而不改内容，比对内容兜底
        // 对齐 TS: 用检测到的编码读取文件，避免 UTF-16LE 内容比对错误
        var detectedEncoding = await FileEncodingDetector.DetectFromFileAsync(filePath, _fs, ct).ConfigureAwait(false);
        var currentContent = await _fs.ReadAllTextAsync(filePath, detectedEncoding, ct).ConfigureAwait(false);
        if (currentContent == readState.Content)
            return null; // 内容未变，安全放行

        return new StaleWriteDetection(lastWriteMs, readState.TimestampMs);
    }
}