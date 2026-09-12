namespace JoinCode.Entry;

/// <summary>
/// 启动时诊断信息 dump 步骤 — 根据 DebugDumpChoice 输出对应类别到 stderr
/// 决策: 放在 SystemPromptApplyStep 之后，确保 system prompt 已应用后再 dump
/// 决策: 输出到 stderr（TerminalHelper.WriteError），不污染 stdout，对脚本和 --json 模式友好
/// 决策: --json 模式下自动关闭（DebugDumpPromptStep 已处理，此处双重保险）
/// 复用 DebugLogRenderer，按 DebugDumpChoice 位标志组合渲染对应部分
/// </summary>
[Register(typeof(IMiddleware<StartupContext>), ServiceLifetime.Singleton)]
internal sealed partial class InitDebugDumpStep : ServiceEntity, IMiddleware<StartupContext>
{
    public async Task InvokeAsync(StartupContext context, MiddlewareDelegate<StartupContext> next, CancellationToken ct)
    {
        var choice = context.DebugDumpChoice;

        // 未选择任何调试信息 → 跳过
        if (choice == DebugDumpSection.None || context.Options.IsJsonMode)
        {
            await next(context, ct).ConfigureAwait(false);
            return;
        }

        var content = await RenderChoiceAsync(choice, context.Host.Services, ct).ConfigureAwait(false);

        // 输出到 stderr，不污染 stdout
        TerminalHelper.WriteError(content);
        Diag.WriteLine($"[STEP] InitDebugDump: 已输出诊断信息到 stderr, 类别={choice} ({(int)choice})");

        await next(context, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 根据位标志组合渲染对应部分 — 复用 DebugLogRenderer
    /// 决策: All 时直接调用 RenderAllAsync（一次调用，避免多次 StringBuilder 分配）
    /// 决策: 非 All 时按位标志逐个渲染，用 StringBuilder 拼接
    /// </summary>
    private static async Task<string> RenderChoiceAsync(DebugDumpSection choice, IServiceProvider services, CancellationToken ct)
    {
        if (choice == DebugDumpSection.All)
        {
            return await DebugLogRenderer.RenderAllAsync(services, ct).ConfigureAwait(false);
        }

        var sb = new StringBuilder();
        if (choice.HasFlag(DebugDumpSection.Init))
        {
            sb.Append(await DebugLogRenderer.RenderInitAsync(services, ct).ConfigureAwait(false));
        }
        if (choice.HasFlag(DebugDumpSection.Error))
        {
            sb.Append(DebugLogRenderer.RenderErrors(services));
        }
        if (choice.HasFlag(DebugDumpSection.Warn))
        {
            sb.Append(DebugLogRenderer.RenderWarningsAndErrors(services));
        }
        if (choice.HasFlag(DebugDumpSection.Log))
        {
            sb.Append(DebugLogRenderer.RenderLogs(services));
        }
        if (choice.HasFlag(DebugDumpSection.Prompt))
        {
            sb.Append(await DebugLogRenderer.RenderSystemPromptAsync(services).ConfigureAwait(false));
        }
        return sb.ToString();
    }
}
