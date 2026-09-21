namespace Tools.Handlers;

/// <summary>
/// 写入防御服务 — 薄编排层，注入 7 个独立 node，适配 node 泛型结果到 <see cref="ToolResult"/> + 诊断。
/// 一切皆为 node/插件（ADR 0104）：每个 node 是独立公共对象，可被任意工具注入使用。
/// 本服务职责：① 注入 node ② 将 node 的 string? error 包装为 ToolResult + 诊断 ③ 提供 Begin() 入口。
/// </summary>
[Register(typeof(WriteDefenseService), ServiceLifetime.Singleton)]
public sealed class WriteDefenseService {
    private readonly SecretGuardNode _secretGuard;
    private readonly FileBackupNode _backupNode;
    private readonly WriteNotifyNode _notifyNode;
    private readonly SandboxGuardNode _sandboxNode;
    private readonly FileStateGuardNode _stateNode;
    private readonly FormatValidatorNode _validatorNode;
    private readonly ITelemetryService? _telemetryService;

    /// <summary>
    /// 构造写入防御服务
    /// </summary>
    /// <param name="secretGuard">团队密钥检测 node</param>
    /// <param name="backupNode">写前备份 node</param>
    /// <param name="notifyNode">写入通知 node</param>
    /// <param name="sandboxNode">沙箱守卫 node</param>
    /// <param name="stateNode">文件状态守卫 node</param>
    /// <param name="validatorNode">格式校验 node</param>
    /// <param name="telemetryService">可选遥测服务</param>
    public WriteDefenseService(
        SecretGuardNode secretGuard,
        FileBackupNode backupNode,
        WriteNotifyNode notifyNode,
        SandboxGuardNode sandboxNode,
        FileStateGuardNode stateNode,
        FormatValidatorNode validatorNode,
        ITelemetryService? telemetryService = null) {
        _secretGuard = secretGuard ?? throw new ArgumentNullException(nameof(secretGuard));
        _backupNode = backupNode ?? throw new ArgumentNullException(nameof(backupNode));
        _notifyNode = notifyNode ?? throw new ArgumentNullException(nameof(notifyNode));
        _sandboxNode = sandboxNode ?? throw new ArgumentNullException(nameof(sandboxNode));
        _stateNode = stateNode ?? throw new ArgumentNullException(nameof(stateNode));
        _validatorNode = validatorNode ?? throw new ArgumentNullException(nameof(validatorNode));
        _telemetryService = telemetryService;
    }

    /// <summary>
    /// 开始构建写入防御链。返回 <see cref="WriteDefense"/> 构建器，链式追加防御步骤。
    /// </summary>
    /// <param name="filePath">原始文件路径</param>
    /// <param name="contentToCheck">待检测内容（密钥检测用；null 跳过）</param>
    /// <param name="operation">操作类型（遥测）</param>
    /// <param name="operationLabel">操作标签（"writing"/"editing"，诊断用）</param>
    /// <param name="oldString">FileEdit 的 old_string（settings 预模拟用，其他传 null）</param>
    /// <param name="newString">FileEdit 的 new_string（settings 预模拟用，其他传 null）</param>
    /// <param name="replaceAll">FileEdit 的 replace_all（settings 预模拟用）</param>
    /// <returns>写入防御链构建器</returns>
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
    //  通用防御步骤（所有写入/编辑工具共用）— 委托各 node，适配 ToolResult
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// UNC 路径拒绝 — 拒绝以 \\ 或 // 开头的路径，防 NTLM 凭据泄露。
    /// 对齐 TS: FileWriteTool.ts L182 / FileEditTool.ts L179。
    /// </summary>
    /// <param name="ctx">写入防御上下文</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>拒绝诊断；通过时返回 null</returns>
    public ValueTask<ToolResult?> RejectUncPath(WriteDefenseContext ctx, CancellationToken ct) {
        if (!PathGuardNode.IsUncPath(ctx.OriginalPath))
            return ValueTask.FromResult<ToolResult?>(null);

        var diag = BuildUncPathWriteRejectedDiagnostic();
        return ValueTask.FromResult<ToolResult?>(
            ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build());
    }

    /// <summary>
    /// 沙箱路径解析 — 委托 SandboxGuardNode，越界构建拒绝诊断。
    /// 解析结果写入 ctx.ResolvedPath 供后续步骤使用。
    /// </summary>
    /// <param name="ctx">写入防御上下文</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>拒绝诊断；通过时返回 null</returns>
    public async ValueTask<ToolResult?> ResolveSandboxAsync(WriteDefenseContext ctx, CancellationToken ct) {
        try {
            ctx.ResolvedPath = await _sandboxNode.ResolvePathAsync(ctx.OriginalPath, ct).ConfigureAwait(false);
            return null;
        } catch (UnauthorizedAccessException ex) {
            var diag = ToolDiagnostic.Create(
                "SandboxViolation",
                $"路径越出沙箱范围: {ex.Message}",
                [new DiagnosticDetail("filePath", ctx.OriginalPath), new DiagnosticDetail("reason", "OutsideSandbox")],
                ["使用沙箱范围内的路径。"]);
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }
    }

    /// <summary>
    /// 团队密钥检测 — 委托 SecretGuardNode，检测到密钥构建拒绝诊断。
    /// ctx.ContentToCheck 为 null 时跳过（如纯删除行不引入新内容）。
    /// </summary>
    /// <param name="ctx">写入防御上下文</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>拒绝诊断；通过时返回 null</returns>
    public ValueTask<ToolResult?> CheckTeamMemSecrets(WriteDefenseContext ctx, CancellationToken ct) {
        var secretError = _secretGuard.CheckSecrets(ctx.ResolvedPath, ctx.ContentToCheck);
        if (secretError is null)
            return ValueTask.FromResult<ToolResult?>(null);

        var diag = BuildTeamMemSecretRejectedDiagnostic(secretError);
        return ValueTask.FromResult<ToolResult?>(
            ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build());
    }

    /// <summary>
    /// 写前读校验 — 委托 FileStateGuardNode，未读构建拒绝诊断。
    /// CLI 无状态模式下跳过校验。
    /// </summary>
    /// <param name="ctx">写入防御上下文</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>拒绝诊断；通过时返回 null</returns>
    public ValueTask<ToolResult?> RequireReadBeforeWrite(WriteDefenseContext ctx, CancellationToken ct) {
        if (_stateNode.HasBeenRead(ctx.ResolvedPath))
            return ValueTask.FromResult<ToolResult?>(null);

        RecordFileMetrics(ctx.Operation, FileOperationResult.Rejected);
        var diag = BuildFileNotReadBeforeWriteDiagnostic();
        return ValueTask.FromResult<ToolResult?>(
            ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build());
    }

    /// <summary>
    /// 脏写保护 — 委托 FileStateGuardNode，检测到外部修改构建拒绝诊断。
    /// </summary>
    /// <param name="ctx">写入防御上下文</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>拒绝诊断；通过时返回 null</returns>
    public async ValueTask<ToolResult?> GuardStaleWriteAsync(WriteDefenseContext ctx, CancellationToken ct) {
        var stale = await _stateNode.CheckStaleWriteAsync(ctx.ResolvedPath, ct).ConfigureAwait(false);
        if (stale is null)
            return null;

        RecordFileMetrics(ctx.Operation, FileOperationResult.Stale);
        var diag = BuildFileModifiedSinceReadDiagnostic(ctx.OperationLabel, ctx.ResolvedPath, stale.Value.LastWriteMs, stale.Value.ReadTimestampMs);
        return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
    }

    /// <summary>
    /// 写前备份 — 委托 FileBackupNode。
    /// </summary>
    /// <param name="ctx">写入防御上下文</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>拒绝诊断；通过时返回 null</returns>
    public async ValueTask<ToolResult?> BackupBeforeWriteAsync(WriteDefenseContext ctx, CancellationToken ct) {
        await _backupNode.BackupAsync(ctx.ResolvedPath, ct).ConfigureAwait(false);
        return null;
    }

    // ════════════════════════════════════════════════════════════════════
    //  FileEdit 独有防御步骤（按需进链）
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Notebook 文件编辑拒绝 — .ipynb 文件必须用 NotebookEdit 工具编辑。
    /// </summary>
    /// <param name="ctx">写入防御上下文</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>拒绝诊断；通过时返回 null</returns>
    public ValueTask<ToolResult?> RejectNotebookEdit(WriteDefenseContext ctx, CancellationToken ct) {
        if (!PathGuardNode.IsNotebookPath(ctx.ResolvedPath))
            return ValueTask.FromResult<ToolResult?>(null);

        var diag = BuildNotebookEditRejectedDiagnostic();
        return ValueTask.FromResult<ToolResult?>(
            ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build());
    }

    /// <summary>
    /// old_string 与 new_string 相同拒绝 — 无变化无需编辑。
    /// </summary>
    /// <param name="ctx">写入防御上下文</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>拒绝诊断；通过时返回 null</returns>
    public ValueTask<ToolResult?> RejectIdenticalStrings(WriteDefenseContext ctx, CancellationToken ct) {
        if (ctx.OldString is null || ctx.NewString is null || ctx.OldString != ctx.NewString)
            return ValueTask.FromResult<ToolResult?>(null);

        var diag = BuildIdenticalStringsDiagnostic();
        return ValueTask.FromResult<ToolResult?>(
            ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build());
    }

    /// <summary>
    /// settings 文件编辑校验 — 委托 FormatValidatorNode。
    /// </summary>
    /// <param name="ctx">写入防御上下文</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>拒绝诊断；通过时返回 null</returns>
    public async ValueTask<ToolResult?> ValidateSettingsEditAsync(WriteDefenseContext ctx, CancellationToken ct) {
        if (ctx.OldString is null || ctx.NewString is null)
            return null; // 非 FileEdit 工具无 old/new_string，跳过

        var settingsError = await _validatorNode.ValidateSettingsEditAsync(
            ctx.ResolvedPath, ctx.OldString, ctx.NewString, ctx.ReplaceAll, ct).ConfigureAwait(false);
        if (settingsError is null)
            return null;

        RecordFileMetrics(ctx.Operation, FileOperationResult.Rejected);
        var diag = BuildSettingsEditRejectedDiagnostic(settingsError);
        return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
    }

    /// <summary>
    /// keyword-sections.json 编辑权限校验 — 委托 FormatValidatorNode。
    /// </summary>
    /// <param name="ctx">写入防御上下文</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>拒绝诊断；通过时返回 null</returns>
    public ValueTask<ToolResult?> ValidateKeywordSectionsEdit(WriteDefenseContext ctx, CancellationToken ct) {
        var error = _validatorNode.ValidateKeywordSectionsEdit(ctx.ResolvedPath);
        if (error is null)
            return ValueTask.FromResult<ToolResult?>(null);

        RecordFileMetrics(ctx.Operation, FileOperationResult.Rejected);
        var diag = BuildKeywordSectionsEditRejectedDiagnostic();
        return ValueTask.FromResult<ToolResult?>(
            ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build());
    }

    /// <summary>
    /// doctor Agent 编辑路径校验 — 委托 FormatValidatorNode。
    /// </summary>
    /// <param name="ctx">写入防御上下文</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>拒绝诊断；通过时返回 null</returns>
    public ValueTask<ToolResult?> ValidateDoctorAgentEdit(WriteDefenseContext ctx, CancellationToken ct) {
        var error = _validatorNode.ValidateDoctorAgentEdit(ctx.ResolvedPath);
        if (error is null)
            return ValueTask.FromResult<ToolResult?>(null);

        RecordFileMetrics(ctx.Operation, FileOperationResult.Rejected);
        var diag = BuildDoctorAgentEditRejectedDiagnostic();
        return ValueTask.FromResult<ToolResult?>(
            ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build());
    }

    // ════════════════════════════════════════════════════════════════════
    //  写入后统一通知 — 委托 WriteNotifyNode
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 写入后统一通知 — 委托 WriteNotifyNode。
    /// 所有写入/编辑类工具在成功完成 I/O 后调用此方法。
    /// </summary>
    /// <param name="filePath">写入的文件路径（沙箱解析后）</param>
    /// <param name="content">写入内容（null 时由 LspFileSync 从磁盘读取）</param>
    /// <param name="operation">操作标签（"write"/"edit" 等）</param>
    /// <param name="opType">操作类型（遥测用）</param>
    public void NotifyWriteComplete(string filePath, string? content, string operation, FileOperationType opType)
        => _notifyNode.NotifyWriteComplete(filePath, content, operation, opType);

    // ════════════════════════════════════════════════════════════════════
    //  私有辅助
    // ════════════════════════════════════════════════════════════════════

    private void RecordFileMetrics(FileOperationType operation, FileOperationResult result)
        => ToolTelemetryHelper.RecordToolCount(_telemetryService, "file.operation.count",
            new Dictionary<string, string> { ["operation"] = operation.ToValue(), ["result"] = result.ToValue() });
}