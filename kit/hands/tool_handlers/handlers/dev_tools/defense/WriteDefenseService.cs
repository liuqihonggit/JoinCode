using static Tools.Handlers.FileToolHandlers;

namespace Tools.Handlers;

/// <summary>
/// 写入防御服务 — 公共对象，注入全部防御依赖，提供统一写入安全防御链 + 写入后通知。
/// 一切皆为 node/插件：本服务是第一个按此哲学提取的公共对象（ADR 0104）。
/// 消费者（FileToolHandlers、NotebookToolHandlers 等）注入此服务，调用 Begin().Then()...ExecuteAsync() 编排防御。
/// </summary>
[Register(typeof(WriteDefenseService), ServiceLifetime.Singleton)]
public sealed class WriteDefenseService : IDisposable
{
    private readonly ISandboxManager? _sandboxManager;
    private readonly ITelemetryService? _telemetryService;
    private readonly IFileStateCache? _fileStateCache;
    private readonly IFileHistoryService? _fileHistoryService;
    private readonly ILspFileSync? _lspFileSync;
    private readonly ITeamMemSecretGuard? _teamMemSecretGuard;
    private readonly IFileWriteListenerRegistry? _fileWriteListenerRegistry;
    private readonly ILspDiagnosticProvider? _lspDiagnosticProvider;
    private readonly ISubAgentContextAccessor? _subAgentContextAccessor;
    private readonly IFileSystem _fs;
    private readonly ILogger<WriteDefenseService>? _logger;
    private readonly CancellationTokenSource _disposeCts = new();

    public WriteDefenseService(
        IFileSystem fs,
        ISandboxManager? sandboxManager = null,
        ITelemetryService? telemetryService = null,
        IFileStateCache? fileStateCache = null,
        IFileHistoryService? fileHistoryService = null,
        ILspFileSync? lspFileSync = null,
        ITeamMemSecretGuard? teamMemSecretGuard = null,
        IFileWriteListenerRegistry? fileWriteListenerRegistry = null,
        ILspDiagnosticProvider? lspDiagnosticProvider = null,
        ISubAgentContextAccessor? subAgentContextAccessor = null,
        ILogger<WriteDefenseService>? logger = null)
    {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _sandboxManager = sandboxManager;
        _telemetryService = telemetryService;
        _fileStateCache = fileStateCache;
        _fileHistoryService = fileHistoryService;
        _lspFileSync = lspFileSync;
        _teamMemSecretGuard = teamMemSecretGuard;
        _fileWriteListenerRegistry = fileWriteListenerRegistry;
        _lspDiagnosticProvider = lspDiagnosticProvider;
        _subAgentContextAccessor = subAgentContextAccessor;
        _logger = logger;
    }

    /// <summary>
    /// 开始构建写入防御链。返回 <see cref="WriteDefense"/> 构建器，链式追加防御步骤。
    /// </summary>
    public WriteDefense Begin(
        string filePath,
        string? contentToCheck,
        FileOperationType operation,
        string operationLabel,
        string? oldString = null,
        string? newString = null,
        bool replaceAll = false)
        => WriteDefense.Begin(filePath, contentToCheck, operation, operationLabel, oldString, newString, replaceAll);

    // ════════════════════════════════════════════════════════════════════
    //  通用防御步骤（所有写入/编辑工具共用）
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// UNC 路径拒绝 — 拒绝以 \\ 或 // 开头的路径，防 NTLM 凭据泄露。
    /// 对齐 TS: FileWriteTool.ts L182 / FileEditTool.ts L179。
    /// </summary>
    public ValueTask<ToolResult?> RejectUncPath(WriteDefenseContext ctx, CancellationToken ct)
    {
        if (!IsUncPath(ctx.OriginalPath))
            return ValueTask.FromResult<ToolResult?>(null);

        var diag = BuildUncPathWriteRejectedDiagnostic();
        return ValueTask.FromResult<ToolResult?>(
            ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build());
    }

    /// <summary>
    /// 沙箱路径解析 — 将原始路径解析为沙箱内绝对路径，越界抛 UnauthorizedAccessException。
    /// 解析结果写入 ctx.ResolvedPath 供后续步骤使用。
    /// </summary>
    public async ValueTask<ToolResult?> ResolveSandboxAsync(WriteDefenseContext ctx, CancellationToken ct)
    {
        try
        {
            ctx.ResolvedPath = await ResolveSandboxPathAsync(ctx.OriginalPath, ct).ConfigureAwait(false);
            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            var diag = ToolDiagnostic.Create(
                "SandboxViolation",
                $"路径越出沙箱范围: {ex.Message}",
                [new DiagnosticDetail("filePath", ctx.OriginalPath), new DiagnosticDetail("reason", "OutsideSandbox")],
                ["使用沙箱范围内的路径。"]);
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }
    }

    /// <summary>
    /// 团队密钥检测 — 拒绝将团队记忆文件中的密钥写入文件。
    /// 对齐 TS: FileWriteTool.ts L157 / FileEditTool.ts L144 — checkTeamMemSecrets。
    /// ctx.ContentToCheck 为 null 时跳过（如纯删除行不引入新内容）。
    /// </summary>
    public ValueTask<ToolResult?> CheckTeamMemSecrets(WriteDefenseContext ctx, CancellationToken ct)
    {
        if (ctx.ContentToCheck is null || _teamMemSecretGuard is null)
            return ValueTask.FromResult<ToolResult?>(null);

        var secretError = _teamMemSecretGuard.CheckTeamMemSecrets(ctx.ResolvedPath, ctx.ContentToCheck);
        if (secretError is null)
            return ValueTask.FromResult<ToolResult?>(null);

        var diag = BuildTeamMemSecretRejectedDiagnostic(secretError);
        return ValueTask.FromResult<ToolResult?>(
            ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build());
    }

    /// <summary>
    /// 写前读校验 — 已存在的文件必须先读取才能写入/编辑。
    /// CLI 无状态模式（mcp_call 单次调用）下 FileStateCache 永远为空，跳过校验。
    /// 对齐 TS: FileWriteTool.ts L198 / FileEditTool.ts L275。
    /// </summary>
    public ValueTask<ToolResult?> RequireReadBeforeWrite(WriteDefenseContext ctx, CancellationToken ct)
    {
        if (_fileStateCache is null || !_fs.FileExists(ctx.ResolvedPath) || TestEnvironmentDetector.ForceNonInteractive)
            return ValueTask.FromResult<ToolResult?>(null);

        if (_fileStateCache.HasBeenRead(ctx.ResolvedPath))
            return ValueTask.FromResult<ToolResult?>(null);

        RecordFileMetrics(ctx.Operation, FileOperationResult.Rejected);
        var diag = BuildFileNotReadBeforeWriteDiagnostic();
        return ValueTask.FromResult<ToolResult?>(
            ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build());
    }

    /// <summary>
    /// 脏写保护（Stale-write guard）— 文件读后被外部修改则拒绝写入。
    /// Windows 时间戳误报回退：用检测编码读取文件比对内容，内容不变则放行。
    /// 对齐 TS: FileWriteTool.ts L212 / FileEditTool.ts L290。
    /// </summary>
    public async ValueTask<ToolResult?> GuardStaleWriteAsync(WriteDefenseContext ctx, CancellationToken ct)
    {
        if (_fileStateCache is null || !_fs.FileExists(ctx.ResolvedPath) || TestEnvironmentDetector.ForceNonInteractive)
            return null;

        var readTimestamp = _fileStateCache.GetReadTimestampMs(ctx.ResolvedPath);
        if (!readTimestamp.HasValue)
            return null;

        var lastWriteMs = new DateTimeOffset(_fs.GetLastWriteTimeUtc(ctx.ResolvedPath)).ToUnixTimeMilliseconds();
        if (lastWriteMs <= readTimestamp.Value + 1000) // 1s 容忍
            return null;

        // 时间戳显示已修改，但 Windows 上云同步/杀毒等会改时间戳而不改内容，比对内容兜底
        var readContent = _fileStateCache.GetReadContent(ctx.ResolvedPath);
        if (readContent is not null)
        {
            // 对齐 TS: 用检测到的编码读取文件，避免 UTF-16LE 内容比对错误
            var detectedEncoding = await FileEncodingDetector.DetectFromFileAsync(ctx.ResolvedPath, _fs, ct).ConfigureAwait(false);
            var currentContent = await _fs.ReadAllTextAsync(ctx.ResolvedPath, detectedEncoding, ct).ConfigureAwait(false);
            if (currentContent == readContent)
                return null; // 内容未变，安全放行
        }

        RecordFileMetrics(ctx.Operation, FileOperationResult.Stale);
        var diag = BuildFileModifiedSinceReadDiagnostic(ctx.OperationLabel, ctx.ResolvedPath, lastWriteMs, readTimestamp.Value);
        return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
    }

    /// <summary>
    /// 写前备份 — 文件存在时备份历史版本，支持撤销恢复。
    /// 对齐 TS: FileWriteTool.ts L259 / FileEditTool.ts L435 — fileHistoryTrackEdit。
    /// </summary>
    public async ValueTask<ToolResult?> BackupBeforeWriteAsync(WriteDefenseContext ctx, CancellationToken ct)
    {
        if (_fileHistoryService is null || !_fs.FileExists(ctx.ResolvedPath))
            return null;

        await _fileHistoryService.BackupBeforeWriteAsync(ctx.ResolvedPath, ct).ConfigureAwait(false);
        return null;
    }

    // ════════════════════════════════════════════════════════════════════
    //  FileEdit 独有防御步骤（按需进链，其他写入工具不调用）
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Notebook 文件编辑拒绝 — .ipynb 文件必须用 NotebookEdit 工具编辑。
    /// 对齐 TS: FileEditTool.ts L266。
    /// </summary>
    public ValueTask<ToolResult?> RejectNotebookEdit(WriteDefenseContext ctx, CancellationToken ct)
    {
        if (!ctx.ResolvedPath.EndsWith(".ipynb", StringComparison.OrdinalIgnoreCase))
            return ValueTask.FromResult<ToolResult?>(null);

        var diag = BuildNotebookEditRejectedDiagnostic();
        return ValueTask.FromResult<ToolResult?>(
            ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build());
    }

    /// <summary>
    /// old_string 与 new_string 相同拒绝 — 无变化无需编辑。
    /// 对齐 TS: FileEditTool.ts L148。
    /// </summary>
    public ValueTask<ToolResult?> RejectIdenticalStrings(WriteDefenseContext ctx, CancellationToken ct)
    {
        if (ctx.OldString is null || ctx.NewString is null || ctx.OldString != ctx.NewString)
            return ValueTask.FromResult<ToolResult?>(null);

        var diag = BuildIdenticalStringsDiagnostic();
        return ValueTask.FromResult<ToolResult?>(
            ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build());
    }

    /// <summary>
    /// settings 文件编辑校验 — 只阻止"从合法变非法"的降级编辑，不阻止修复。
    /// 预模拟编辑：用 old_string/new_string/replace_all 计算编辑后内容，再校验合法性。
    /// 对齐 TS: FileEditTool.ts L346 — validateInputForSettingsFileEdit。
    /// </summary>
    public async ValueTask<ToolResult?> ValidateSettingsEditAsync(WriteDefenseContext ctx, CancellationToken ct)
    {
        if (!SettingsEditValidator.IsJccSettingsPath(ctx.ResolvedPath) || !_fs.FileExists(ctx.ResolvedPath))
            return null;
        if (ctx.OldString is null || ctx.NewString is null)
            return null; // 非 FileEdit 工具无 old/new_string，跳过

        var detectedEncoding = await FileEncodingDetector.DetectFromFileAsync(ctx.ResolvedPath, _fs, ct).ConfigureAwait(false);
        var originalContent = await _fs.ReadAllTextAsync(ctx.ResolvedPath, detectedEncoding, ct).ConfigureAwait(false);

        // 对齐 TS: 预模拟编辑 — 使用与 FileEditTool 相同的替换逻辑
        var updatedContent = ctx.ReplaceAll
            ? originalContent.Replace(ctx.OldString, ctx.NewString)
            : ReplaceFirst(originalContent, ctx.OldString, ctx.NewString);

        var settingsError = SettingsEditValidator.ValidateEdit(ctx.ResolvedPath, originalContent, updatedContent);
        if (settingsError is null)
            return null;

        RecordFileMetrics(ctx.Operation, FileOperationResult.Rejected);
        var diag = BuildSettingsEditRejectedDiagnostic(settingsError);
        return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
    }

    /// <summary>
    /// keyword-sections.json 编辑权限校验 — 仅 keywordMaintenance Agent 可编辑。
    /// </summary>
    public ValueTask<ToolResult?> ValidateKeywordSectionsEdit(WriteDefenseContext ctx, CancellationToken ct)
    {
        if (!IsKeywordSectionsPath(ctx.ResolvedPath) || !_fs.FileExists(ctx.ResolvedPath))
            return ValueTask.FromResult<ToolResult?>(null);

        var currentAgentType = _subAgentContextAccessor?.Current?.Variant?.ToValue() ?? _subAgentContextAccessor?.Current?.Role.ToValue();
        if (currentAgentType is null || currentAgentType.Equals("keywordMaintenance", StringComparison.OrdinalIgnoreCase))
            return ValueTask.FromResult<ToolResult?>(null);

        RecordFileMetrics(ctx.Operation, FileOperationResult.Rejected);
        var diag = BuildKeywordSectionsEditRejectedDiagnostic();
        return ValueTask.FromResult<ToolResult?>(
            ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build());
    }

    /// <summary>
    /// doctor Agent 编辑路径校验 — 仅允许编辑 .jcc/diag/、.jcc/reflexion/、worktree 内文件。
    /// </summary>
    public ValueTask<ToolResult?> ValidateDoctorAgentEdit(WriteDefenseContext ctx, CancellationToken ct)
    {
        var agentType = _subAgentContextAccessor?.Current?.Variant?.ToValue() ?? _subAgentContextAccessor?.Current?.Role.ToValue();
        if (agentType is null || !agentType.Equals("doctor", StringComparison.OrdinalIgnoreCase))
            return ValueTask.FromResult<ToolResult?>(null);
        if (IsDoctorAllowedEditPath(ctx.ResolvedPath))
            return ValueTask.FromResult<ToolResult?>(null);

        RecordFileMetrics(ctx.Operation, FileOperationResult.Rejected);
        var diag = BuildDoctorAgentEditRejectedDiagnostic();
        return ValueTask.FromResult<ToolResult?>(
            ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build());
    }

    // ════════════════════════════════════════════════════════════════════
    //  写入后统一通知
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 写入后统一通知 — 清除 LSP 诊断 + 通知 LSP 文件变更 + 记录遥测 + 通知写入监听器。
    /// 所有写入/编辑类工具在成功完成 I/O 后调用此方法，消除各工具尾部重复通知代码。
    /// 对齐 TS: FileWriteTool.ts L307-332 / FileEditTool.ts L493-520。
    /// </summary>
    /// <param name="filePath">写入的文件路径（沙箱解析后）</param>
    /// <param name="content">写入内容（null 时由 LspFileSync 从磁盘读取，如 FileEdit 场景）</param>
    /// <param name="operation">操作标签（"write"/"edit"/"edit-regex"/"insert-lines"/"delete-lines"/"batch-edit"/"apply-patch"）</param>
    /// <param name="opType">操作类型（遥测 RecordFileMetrics 用）</param>
    public void NotifyWriteComplete(string filePath, string? content, string operation, FileOperationType opType)
    {
        // 1. 清除已投递 LSP 诊断 — 让新诊断能重新展示
        _lspDiagnosticProvider?.ClearDeliveredForFile($"file://{filePath}");

        // 2. 通知 LSP 服务器文件变更（fire-and-forget）: changeFile → saveFile
        NotifyLspFileChange(filePath, content);

        // 3. 记录遥测
        RecordFileMetrics(opType, FileOperationResult.Ok);

        // 4. 通知文件写入监听器 — Worker 改文件时触发意图上报
        NotifyFileWrite(filePath, operation);
    }

    // ════════════════════════════════════════════════════════════════════
    //  私有辅助方法
    // ════════════════════════════════════════════════════════════════════

    private static bool IsUncPath(string filePath)
        => filePath.StartsWith("\\\\", StringComparison.Ordinal) || filePath.StartsWith("//", StringComparison.Ordinal);

    private static bool IsKeywordSectionsPath(string filePath)
        => !string.IsNullOrEmpty(filePath)
           && Path.GetFileName(filePath).Equals("keyword-sections.json", StringComparison.OrdinalIgnoreCase);

    /// <summary>doctor Agent 允许编辑的路径 — .jcc/diag/、.jcc/reflexion/、worktree 内文件</summary>
    private static bool IsDoctorAllowedEditPath(string filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return false;
        var normalized = filePath.Replace('\\', '/');
        return normalized.Contains("/.jcc/diag/", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("/.jcc/reflexion/", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("/worktree/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>替换字符串中第一个匹配项（用于预模拟编辑）。对齐 TS: file.replace。</summary>
    private static string ReplaceFirst(string text, string search, string replace)
    {
        var index = text.IndexOf(search, StringComparison.Ordinal);
        if (index < 0) return text;
        return string.Concat(text.AsSpan(0, index), replace, text.AsSpan(index + search.Length));
    }

    private async Task<string> ResolveSandboxPathAsync(string path, CancellationToken cancellationToken)
    {
        if (_sandboxManager == null || !_sandboxManager.IsInSandbox) return path;
        var sandboxId = _sandboxManager.CurrentSandboxId;
        if (sandboxId is null) return path;

        var resolvedPath = _sandboxManager.ResolvePath(path, sandboxId);
        var isInSandbox = await _sandboxManager.ActiveProvider!.IsPathInSandboxAsync(resolvedPath, sandboxId, cancellationToken).ConfigureAwait(false);
        if (!isInSandbox)
            throw new UnauthorizedAccessException($"Path '{path}' is outside the sandbox scope");
        return resolvedPath;
    }

    private void RecordFileMetrics(FileOperationType operation, FileOperationResult result)
        => ToolTelemetryHelper.RecordToolCount(_telemetryService, "file.operation.count",
            new Dictionary<string, string> { ["operation"] = operation.ToValue(), ["result"] = result.ToValue() });

    private void NotifyFileWrite(string filePath, string operation)
    {
        if (_fileWriteListenerRegistry is null) return;
        var agentId = _subAgentContextAccessor?.Current?.AgentId ?? "main";
        try
        {
            _fileWriteListenerRegistry.Notify(new FileWriteEventArgs { FilePath = filePath, Operation = operation, AgentId = agentId });
        }
        catch (Exception ex)
        {
            _logger?.LogWarning("通知文件写入监听器失败: {Message}", ex.Message);
        }
    }

    /// <summary>通知 LSP 服务器文件变更（fire-and-forget）。对齐 TS: lspManager.changeFile + saveFile。</summary>
    private void NotifyLspFileChange(string filePath, string? content)
    {
        if (_lspFileSync is null) return;

        var ct = _disposeCts.Token;
        // fire-and-forget: 不阻塞主流程，错误在 LspFileSync 内部处理
        _ = Task.Run(async () =>
        {
            try
            {
                var changeContent = content;
                if (changeContent is null)
                {
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
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _telemetryService?.RecordCount("file.lsp_notify.error", new Dictionary<string, string>
                {
                    ["operation"] = "change_and_save",
                    ["error"] = ex.GetType().Name
                }, description: "LSP file change notification error");
            }
        }, ct).ConfigureAwait(false);
    }

    public void Dispose() => _disposeCts.CancelAndDisposeSafe(_logger);
}
