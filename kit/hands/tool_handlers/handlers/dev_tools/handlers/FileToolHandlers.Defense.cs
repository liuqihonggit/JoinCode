namespace Tools.Handlers;

/// <summary>
/// 写入防御上下文 — 在防御链各步骤间传递的可变状态。
/// ResolvedPath 在 <see cref="FileToolHandlers.ResolveSandboxAsync"/> 步骤后填充。
/// </summary>
internal sealed class WriteDefenseContext
{
    /// <summary>原始文件路径（调用方传入，未经沙箱解析）</summary>
    public required string OriginalPath { get; init; }

    /// <summary>沙箱解析后的绝对路径（ResolveSandbox 步骤填充，初始等于 OriginalPath）</summary>
    public string ResolvedPath { get; set; } = string.Empty;

    /// <summary>待检测内容（用于团队密钥检测；null 表示跳过密钥检测，如纯删除行）</summary>
    public string? ContentToCheck { get; init; }

    /// <summary>操作类型（遥测 RecordFileMetrics 用）</summary>
    public required FileOperationType Operation { get; init; }

    /// <summary>操作标签（"writing"/"editing"，诊断消息中展示）</summary>
    public required string OperationLabel { get; init; }

    // ── FileEdit 独有参数（settings 预模拟编辑用，其他工具传 null/default） ──

    /// <summary>旧字符串（FileEdit 的 old_string，settings 预模拟编辑用）</summary>
    public string? OldString { get; init; }

    /// <summary>新字符串（FileEdit 的 new_string，settings 预模拟编辑用）</summary>
    public string? NewString { get; init; }

    /// <summary>是否替换全部（FileEdit 的 replace_all，settings 预模拟编辑用）</summary>
    public bool ReplaceAll { get; init; }
}

/// <summary>
/// 写入防御步骤委托 — 输入上下文，返回拒绝诊断（null 表示通过）。
/// 每个防御步骤是一个有名函数，签名统一，可链式追加到 <see cref="WriteDefense"/>。
/// </summary>
internal delegate ValueTask<ToolResult?> WriteDefenseStepAsync(
    WriteDefenseContext ctx, CancellationToken ct);

/// <summary>
/// 写入防御链构建器 — 链式追加防御步骤，<see cref="ExecuteAsync"/> 顺序执行任一短路。
/// 所有写入/编辑类工具（FileWrite/FileEdit/FileEditRegex/FileInsertLines/FileDeleteLines/FileBatchEdit/FileApplyPatch）
/// 统一通过 <c>WriteDefense.Begin(...).Then(...).ExecuteAsync(ct)</c> 编排安全防御。
/// </summary>
internal sealed class WriteDefense
{
    private readonly WriteDefenseContext _context;
    private readonly List<WriteDefenseStepAsync> _steps = new();

    private WriteDefense(WriteDefenseContext context) => _context = context;

    /// <summary>
    /// 开始构建写入防御链。
    /// </summary>
    /// <param name="filePath">原始文件路径</param>
    /// <param name="contentToCheck">待检测内容（密钥检测用；null 跳过）</param>
    /// <param name="operation">操作类型（遥测）</param>
    /// <param name="operationLabel">操作标签（"writing"/"editing"，诊断用）</param>
    /// <param name="oldString">FileEdit 的 old_string（settings 预模拟用，其他传 null）</param>
    /// <param name="newString">FileEdit 的 new_string（settings 预模拟用，其他传 null）</param>
    /// <param name="replaceAll">FileEdit 的 replace_all（settings 预模拟用）</param>
    public static WriteDefense Begin(
        string filePath,
        string? contentToCheck,
        FileOperationType operation,
        string operationLabel,
        string? oldString = null,
        string? newString = null,
        bool replaceAll = false)
    {
        return new WriteDefense(new WriteDefenseContext
        {
            OriginalPath = filePath,
            ResolvedPath = filePath,
            ContentToCheck = contentToCheck,
            Operation = operation,
            OperationLabel = operationLabel,
            OldString = oldString,
            NewString = newString,
            ReplaceAll = replaceAll
        });
    }

    /// <summary>
    /// 追加一个防御步骤。步骤按追加顺序执行，任一返回非 null 即短路停止。
    /// </summary>
    public WriteDefense Then(WriteDefenseStepAsync step)
    {
        _steps.Add(step);
        return this;
    }

    /// <summary>
    /// 执行防御链。顺序调用各步骤，任一步骤返回非 null 拒绝结果即短路返回。
    /// 全部通过则返回 (context, null)。
    /// </summary>
    public async ValueTask<(WriteDefenseContext Context, ToolResult? Rejection)> ExecuteAsync(CancellationToken ct)
    {
        foreach (var step in _steps)
        {
            ct.ThrowIfCancellationRequested();
            var rejection = await step(_context, ct).ConfigureAwait(false);
            if (rejection is not null)
                return (_context, rejection);
        }
        return (_context, null);
    }
}

/// <summary>
/// FileToolHandlers 写入防御链 — partial class，分离统一安全防御逻辑。
/// 包含全部防御步骤有名函数（按需进链）和写入后统一通知方法。
/// </summary>
public partial class FileToolHandlers
{
    // ════════════════════════════════════════════════════════════════════
    //  通用防御步骤（所有写入/编辑工具共用）
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// UNC 路径拒绝 — 拒绝以 \\ 或 // 开头的路径，防 NTLM 凭据泄露。
    /// 对齐 TS: FileWriteTool.ts L182 / FileEditTool.ts L179。
    /// </summary>
    private ValueTask<ToolResult?> RejectUncPath(WriteDefenseContext ctx, CancellationToken ct)
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
    private async ValueTask<ToolResult?> ResolveSandboxAsync(WriteDefenseContext ctx, CancellationToken ct)
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
    private ValueTask<ToolResult?> CheckTeamMemSecrets(WriteDefenseContext ctx, CancellationToken ct)
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
    private ValueTask<ToolResult?> RequireReadBeforeWrite(WriteDefenseContext ctx, CancellationToken ct)
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
    private async ValueTask<ToolResult?> GuardStaleWriteAsync(WriteDefenseContext ctx, CancellationToken ct)
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
    private async ValueTask<ToolResult?> BackupBeforeWriteAsync(WriteDefenseContext ctx, CancellationToken ct)
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
    private ValueTask<ToolResult?> RejectNotebookEdit(WriteDefenseContext ctx, CancellationToken ct)
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
    private ValueTask<ToolResult?> RejectIdenticalStrings(WriteDefenseContext ctx, CancellationToken ct)
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
    private async ValueTask<ToolResult?> ValidateSettingsEditAsync(WriteDefenseContext ctx, CancellationToken ct)
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
    private ValueTask<ToolResult?> ValidateKeywordSectionsEdit(WriteDefenseContext ctx, CancellationToken ct)
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
    private ValueTask<ToolResult?> ValidateDoctorAgentEdit(WriteDefenseContext ctx, CancellationToken ct)
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
    private void NotifyWriteComplete(string filePath, string? content, string operation, FileOperationType opType)
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
}
