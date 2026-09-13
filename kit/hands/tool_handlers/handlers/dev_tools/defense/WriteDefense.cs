namespace Tools.Handlers;

/// <summary>
/// 写入防御上下文 — 在防御链各步骤间传递的可变状态。
/// ResolvedPath 在 <see cref="WriteDefenseService.ResolveSandboxAsync"/> 步骤后填充。
/// </summary>
public sealed class WriteDefenseContext
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
public delegate ValueTask<ToolResult?> WriteDefenseStepAsync(
    WriteDefenseContext ctx, CancellationToken ct);

/// <summary>
/// 写入防御链构建器 — 链式追加防御步骤，<see cref="ExecuteAsync"/> 顺序执行任一短路。
/// 所有写入/编辑类工具统一通过 <c>WriteDefense.Begin(...).Then(...).ExecuteAsync(ct)</c> 编排安全防御。
/// </summary>
public sealed class WriteDefense
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
