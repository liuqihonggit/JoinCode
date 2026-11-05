namespace Tools.Handlers;

/// <summary>
/// 写入通知 node — 独立公共对象，提供写入后统一通知：LSP 诊断清除 + LSP 文件变更 + 遥测 + 写入监听器。
/// 任意写入文件的工具可注入此 node 在写入完成后通知。
/// 对齐 TS: FileWriteTool.ts L307-332 / FileEditTool.ts L493-520。
/// </summary>
[Register(typeof(WriteNotifyNode), ServiceLifetime.Singleton)]
public sealed class WriteNotifyNode : IDisposable {
    private readonly ILspFileSync? _lspFileSync;
    private readonly ILspDiagnosticProvider? _lspDiagnosticProvider;
    private readonly ITelemetryService? _telemetryService;
    private readonly IFileWriteListenerRegistry? _fileWriteListenerRegistry;
    private readonly ISubAgentContextAccessor? _subAgentContextAccessor;
    private readonly IFileSystem _fs;
    private readonly ILogger<WriteNotifyNode>? _logger;
    private readonly CancellationTokenSource _disposeCts = new();

    /// <summary>
    /// 构造写入通知 node
    /// </summary>
    /// <param name="fs">文件系统抽象</param>
    /// <param name="lspFileSync">可选的 LSP 文件同步服务</param>
    /// <param name="lspDiagnosticProvider">可选的 LSP 诊断提供者</param>
    /// <param name="telemetryService">可选遥测服务</param>
    /// <param name="fileWriteListenerRegistry">可选的文件写入监听器注册表</param>
    /// <param name="subAgentContextAccessor">可选的子 Agent 上下文访问器</param>
    /// <param name="logger">可选日志记录器</param>
    public WriteNotifyNode(
        IFileSystem fs,
        ILspFileSync? lspFileSync = null,
        ILspDiagnosticProvider? lspDiagnosticProvider = null,
        ITelemetryService? telemetryService = null,
        IFileWriteListenerRegistry? fileWriteListenerRegistry = null,
        ISubAgentContextAccessor? subAgentContextAccessor = null,
        ILogger<WriteNotifyNode>? logger = null) {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _lspFileSync = lspFileSync;
        _lspDiagnosticProvider = lspDiagnosticProvider;
        _telemetryService = telemetryService;
        _fileWriteListenerRegistry = fileWriteListenerRegistry;
        _subAgentContextAccessor = subAgentContextAccessor;
        _logger = logger;
    }

    /// <summary>
    /// 写入后统一通知 — 清除 LSP 诊断 + 通知 LSP 文件变更 + 记录遥测 + 通知写入监听器。
    /// </summary>
    /// <param name="filePath">写入的文件路径（沙箱解析后）</param>
    /// <param name="content">写入内容（null 时由 LspFileSync 从磁盘读取，如 FileEdit 场景）</param>
    /// <param name="operation">操作标签（"write"/"edit"/"edit-regex" 等）</param>
    /// <param name="opType">操作类型（遥测 RecordFileMetrics 用）</param>
    public void NotifyWriteComplete(string filePath, string? content, string operation, FileOperationType opType) {
        // 1. 清除已投递 LSP 诊断 — 让新诊断能重新展示
        _lspDiagnosticProvider?.ClearDeliveredForFile($"file://{filePath}");

        // 2. 通知 LSP 服务器文件变更（fire-and-forget）: changeFile → saveFile
        NotifyLspFileChange(filePath, content);

        // 3. 记录遥测
        RecordFileMetrics(opType, FileOperationResult.Ok);

        // 4. 通知文件写入监听器 — Worker 改文件时触发意图上报
        NotifyFileWrite(filePath, operation);
    }

    private void RecordFileMetrics(FileOperationType operation, FileOperationResult result)
        => ToolTelemetryHelper.RecordToolCount(_telemetryService, "file.operation.count",
            new Dictionary<string, string> { ["operation"] = operation.ToValue(), ["result"] = result.ToValue() });

    private void NotifyFileWrite(string filePath, string operation) {
        if (_fileWriteListenerRegistry is null) return;
        var agentId = _subAgentContextAccessor?.Current?.AgentId ?? "main";
        try {
            _fileWriteListenerRegistry.Notify(new FileWriteEventArgs { FilePath = filePath, Operation = operation, AgentId = agentId });
        } catch (Exception ex) {
            _logger?.LogWarning("通知文件写入监听器失败: {Message}", ex.Message);
        }
    }

    /// <summary>通知 LSP 服务器文件变更（fire-and-forget）。对齐 TS: lspManager.changeFile + saveFile。</summary>
    private void NotifyLspFileChange(string filePath, string? content) {
        if (_lspFileSync is null) return;

        var ct = _disposeCts.Token;
        // fire-and-forget: 不阻塞主流程，错误在 LspFileSync 内部处理
        _ = Task.Run(async () => {
            try {
                var changeContent = content;
                if (changeContent is null) {
                    // FileEdit 场景：从磁盘读取编辑后的内容（检测编码）
                    var encoding = await FileEncodingDetector.DetectFromFileAsync(filePath, _fs).ConfigureAwait(false);
                    changeContent = await _fs.ReadAllTextAsync(filePath, encoding).ConfigureAwait(false);
                }

                await _lspFileSync.ChangeDocumentAsync(
                    filePath,
                    [new TextDocumentContentChangeEvent { Text = changeContent }],
                    ct).WaitAsync(TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);

                await _lspFileSync.SaveDocumentAsync(filePath, ct)
                    .WaitAsync(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
            } catch (OperationCanceledException) { } catch (Exception ex) {
                _telemetryService?.RecordCount("file.lsp_notify.error", new Dictionary<string, string> {
                    ["operation"] = "change_and_save",
                    ["error"] = ex.GetType().Name
                }, description: "LSP file change notification error");
            }
        }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 释放资源 — 取消待处理的 LSP 通知
    /// </summary>
    public void Dispose() => _disposeCts.CancelAndDisposeSafe(_logger);
}