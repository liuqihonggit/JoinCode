namespace Tools.Handlers;

/// <summary>
/// 文件备份 node — 独立公共对象，包装 <see cref="IFileHistoryService"/>，提供写前备份能力。
/// 任意修改文件的工具（文件写入、文件删除、notebook 编辑等）可注入此 node 在修改前备份历史版本。
/// </summary>
[Register(typeof(FileBackupNode), ServiceLifetime.Singleton)]
public sealed class FileBackupNode {
    private readonly IFileHistoryService? _historyService;
    private readonly IFileSystem _fs;
    private readonly ILogger<FileBackupNode>? _logger;

    /// <summary>
    /// 构造文件备份 node
    /// </summary>
    /// <param name="fs">文件系统抽象</param>
    /// <param name="historyService">可选的文件历史服务</param>
    /// <param name="logger">可选日志记录器</param>
    public FileBackupNode(
        IFileSystem fs,
        IFileHistoryService? historyService = null,
        ILogger<FileBackupNode>? logger = null) {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _historyService = historyService;
        _logger = logger;
    }

    /// <summary>
    /// 文件存在时备份历史版本，支持撤销恢复。
    /// 对齐 TS: FileWriteTool.ts L259 / FileEditTool.ts L435 — fileHistoryTrackEdit。
    /// 文件不存在或备份服务未注入时安全跳过。
    /// </summary>
    /// <param name="filePath">文件路径（沙箱解析后）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    public async ValueTask BackupAsync(string filePath, CancellationToken ct) {
        if (_historyService is null || !_fs.FileExists(filePath))
            return;

        await _historyService.BackupBeforeWriteAsync(filePath, ct).ConfigureAwait(false);
    }
}