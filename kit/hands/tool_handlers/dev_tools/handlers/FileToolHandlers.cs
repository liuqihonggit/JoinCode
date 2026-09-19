

namespace Tools.Handlers;

/// <summary>
/// 文件工具处理器 — 提供 Read/Write/Edit/List/Patch/Snip/Delete/Diagnostics 等文件操作工具
/// </summary>
[McpToolDispatch(ToolCategory.File)]
public partial class FileToolHandlers : IDisposable {
    private readonly CancellationTokenSource _disposeCts = new();
    private bool _disposed;

    private const string MalwareReminder = """

        <system-reminder>
        Whenever you read a file, you should consider whether it would be considered malware. You CAN and SHOULD provide analysis of malware, what it is doing. But you MUST refuse to improve or augment the code. You can still analyze existing code, write reports, or behavior about the code.
        </system-reminder>
        """;

    private readonly IFileOperationService _fileOperationService;
    private readonly IFileSystem _fs;
    private readonly FileToolHandlersContext _ctx;
    private readonly WriteDefenseService _writeDefense;
    private readonly FileTelemetryRecorder _telemetry;
    private readonly FilePathResolver _pathResolver;
    private readonly FileSpecialFormatReader _specialReader;
    private readonly ILogger<FileToolHandlers>? _logger;

    /// <summary>
    /// 构造文件工具处理器
    /// </summary>
    /// <param name="fileOperationService">文件操作服务，提供读写编辑等基础能力</param>
    /// <param name="fs">文件系统抽象</param>
    /// <param name="context">可选依赖上下文，聚合各服务和逻辑组件</param>
    /// <param name="logger">可选日志记录器</param>
    public FileToolHandlers(
        IFileOperationService fileOperationService,
        IFileSystem fs,
        FileToolHandlersContext? context = null,
        ILogger<FileToolHandlers>? logger = null) {
        _fileOperationService = fileOperationService ?? throw new ArgumentNullException(nameof(fileOperationService));
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _logger = logger;
        _ctx = context ?? new FileToolHandlersContext();
        _telemetry = new FileTelemetryRecorder(_ctx.TelemetryService);
        _pathResolver = new FilePathResolver(_ctx.SandboxManager);
        _specialReader = new FileSpecialFormatReader(_fs, _ctx.FileOperationConfig!, _ctx.FileStateCache, _telemetry, _pathResolver, _logger);
        // FileOperationConfig fallback: 保证 _ctx.FileOperationConfig 非 null（对齐原 _ctx.FileOperationConfig = context?.FileOperationConfig ?? new()）
        if (_ctx.FileOperationConfig is null)
            _ctx = _ctx with { FileOperationConfig = new FileOperationConfig() };
        // WriteDefenseService — 优先从 DI 获取，否则用当前依赖现场构造 node 再注入（测试场景）
        _writeDefense = _ctx.WriteDefenseService
            ?? new WriteDefenseService(
                new SecretGuardNode(_ctx.TeamMemSecretGuard),
                new FileBackupNode(_fs, _ctx.FileHistoryService),
                new WriteNotifyNode(_fs, _ctx.LspFileSync, _ctx.LspDiagnosticProvider, _ctx.TelemetryService,
                    _ctx.FileWriteListenerRegistry, _ctx.SubAgentContextAccessor),
                new SandboxGuardNode(_ctx.SandboxManager),
                new FileStateGuardNode(_fs, _ctx.FileStateCache),
                new FormatValidatorNode(_fs, _ctx.SubAgentContextAccessor),
                _ctx.TelemetryService);
    }

    /// <summary>
    /// 释放资源 — 取消所有待处理的操作
    /// </summary>
    public void Dispose() {
        if (_disposed) return;
        _disposed = true;
        _disposeCts.CancelAndDisposeSafe(_logger);
    }

    private static bool IsBlockedDevicePath(string filePath) => FilePathResolver.IsBlockedDevicePath(filePath);

    /// <summary>
    /// 替换字符串中第一个匹配项（用于预模拟编辑）。
    /// 对齐 TS: file.replace(actualOldString, new_string) — 非替换所有时只替换第一个匹配
    /// </summary>
    private static string ReplaceFirst(string text, string search, string replace) {
        var index = text.IndexOf(search, StringComparison.Ordinal);
        if (index < 0) return text;
        return string.Concat(text.AsSpan(0, index), replace, text.AsSpan(index + search.Length));
    }

    internal static string AddLineNumbers(string content, int startLine, bool compact) {
        if (string.IsNullOrEmpty(content)) {
            return string.Empty;
        }

        var lines = content.Split(['\n'], StringSplitOptions.None);

        // 紧凑格式：行号 + 制表符 + 内容（cat -n 风格）
        // 对齐 TS: isCompactLinePrefixEnabled — LLM 训练数据匹配度高，每行省 ~5 空格 token
        if (compact) {
            var compactSb = new StringBuilder(content.Length + lines.Length * 4);
            for (var i = 0; i < lines.Length; i++) {
                compactSb.Append(startLine + i);
                compactSb.Append('\t');
                compactSb.AppendLine(lines[i]);
            }

            return compactSb.ToString();
        }

        // 宽格式：行号右对齐到 ≥6 位 + 管头 → + 内容
        var maxLineNum = startLine + lines.Length - 1;
        var maxDigits = maxLineNum.ToString().Length;
        var padWidth = Math.Max(maxDigits, 6);

        var sb = new StringBuilder(content.Length + lines.Length * (padWidth + 2));
        for (var i = 0; i < lines.Length; i++) {
            var lineNum = startLine + i;
            sb.Append(lineNum.ToString().PadLeft(padWidth));
            sb.Append('\u2192');
            sb.AppendLine(lines[i]);
        }

        return sb.ToString();
    }

    private void RecordFileMetrics(FileOperationType operation, FileOperationResult result) => _telemetry.RecordFileMetrics(operation, result);

    /// <summary>
    /// 记录文件读取详细遥测。
    /// 对齐 TS: tengu_session_file_read — 文本文件读取详情（行数/字节数/扩展名/会话文件类型）
    /// 对齐 TS: tengu_file_operation — 通用文件操作（路径哈希脱敏）
    /// </summary>
    private void RecordFileReadTelemetry(string filePath, string content, int totalLines, int readLines, int? offset, int? limit) => _telemetry.RecordFileReadTelemetry(filePath, content, totalLines, readLines, offset, limit);

    /// <summary>
    /// 记录 PDF 读取遥测。
    /// 对齐 TS: tengu_pdf_page_extraction — PDF 页面提取事件
    /// </summary>
    private void RecordPdfReadTelemetry(string filePath, long fileSize, bool success) => _telemetry.RecordPdfReadTelemetry(filePath, fileSize, success);

    /// <summary>
    /// 记录文件操作遥测（路径哈希脱敏）。
    /// 对齐 TS: tengu_file_operation — 通用文件操作事件
    /// </summary>
    private void RecordFileOperationTelemetry(string filePath, string operation) => _telemetry.RecordFileOperationTelemetry(filePath, operation);

    private async Task<string> ResolveSandboxPathAsync(string path, CancellationToken cancellationToken) => await _pathResolver.ResolveSandboxPathAsync(path, cancellationToken).ConfigureAwait(false);
}