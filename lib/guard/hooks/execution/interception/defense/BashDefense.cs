namespace Core.Hooks.Execution.Interception.Defense;

/// <summary>
/// Bash 防御步骤委托 — 输入上下文，返回拒绝诊断（null 表示通过）。
/// <para>
/// 每个防御步骤是一个有名函数，签名统一，可链式追加到 <see cref="BashDefense"/>。
/// 对齐 <c>WriteDefenseStepAsync</c> 模式（ADR 0104：一切皆为 node/插件）。
/// </para>
/// </summary>
/// <param name="ctx">Bash 防御上下文</param>
/// <param name="ct">取消令牌</param>
/// <returns>拒绝诊断；通过时返回 null</returns>
public delegate ValueTask<ToolResult?> BashDefenseStepAsync(
    BashDefenseContext ctx, CancellationToken ct);

/// <summary>
/// Bash 防御链构建器 — 链式追加防御步骤，<see cref="ExecuteAsync"/> 顺序执行任一短路。
/// <para>
/// 所有 bash 命令统一通过 <c>BashDefense.Begin(...).Then(...).ExecuteAsync(ct)</c> 编排安全防御。
/// 对齐 <c>WriteDefense</c> 模式（ADR 0104：一切皆为 node/插件）。
/// </para>
/// <para>
/// 与 <see cref="CommandInterceptionDispatcher"/> 的关系：
/// <list type="bullet">
/// <item>Dispatcher — DI 自动收集 ICommandGuard，按 Priority 全量执行，适合固定全局守卫</item>
/// <item>BashDefense — 链式构建器，手动 .Then() 组装，适合可插拔的按场景配置的拦截设备</item>
/// </list>
/// 两者共存：Dispatcher 处理不适合 node 化的守卫（GitCommit/Heredoc 等），BashDefense 处理 MTP 扰动防御链。
/// </para>
/// </summary>
public sealed class BashDefense
{
    private readonly BashDefenseContext _context;
    private readonly List<BashDefenseStepAsync> _steps = new();

    private BashDefense(BashDefenseContext context) => _context = context;

    /// <summary>
    /// 开始构建 Bash 防御链。
    /// </summary>
    /// <param name="command">原始命令字符串</param>
    /// <param name="workingDirectory">工作目录路径</param>
    /// <param name="shellKind">Shell 类型</param>
    /// <param name="confirmMode">确认模式（默认 None）</param>
    /// <param name="confirmedCommand">已确认命令（二次确认场景，默认 null）</param>
    /// <param name="argvHash">argv hash（防意图反推，默认 null）</param>
    /// <returns>Bash 防御链构建器</returns>
    public static BashDefense Begin(
        string command,
        string workingDirectory,
        SystemActuatorKind shellKind,
        GuardConfirmMode confirmMode = GuardConfirmMode.None,
        string? confirmedCommand = null,
        string? argvHash = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        return new BashDefense(new BashDefenseContext
        {
            OriginalCommand = command,
            CurrentCommand = command,
            WorkingDirectory = workingDirectory,
            ShellKind = shellKind,
            ConfirmMode = confirmMode,
            ConfirmedCommand = confirmedCommand,
            ArgvHash = argvHash
        });
    }

    /// <summary>
    /// 追加一个防御步骤。步骤按追加顺序执行，任一返回非 null 即短路停止。
    /// </summary>
    /// <param name="step">防御步骤（有名函数，非 lambda）</param>
    /// <returns>自身，支持链式调用</returns>
    public BashDefense Then(BashDefenseStepAsync step)
    {
        ArgumentNullException.ThrowIfNull(step);
        _steps.Add(step);
        return this;
    }

    /// <summary>
    /// 执行防御链。顺序调用各步骤，任一步骤返回非 null 拒绝结果即短路返回。
    /// 全部通过则返回 (context, null)。
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>上下文和拒绝结果（null 表示全部通过）</returns>
    public async ValueTask<(BashDefenseContext Context, ToolResult? Rejection)> ExecuteAsync(CancellationToken ct)
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
