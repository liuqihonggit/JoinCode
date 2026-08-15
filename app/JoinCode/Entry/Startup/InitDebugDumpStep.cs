namespace JoinCode.Entry;

/// <summary>
/// 启动时诊断信息 dump 步骤 — 当 --debuglog 启用时，在 SystemPromptApplyStep 之后自动输出等价 /debuglog -a 的内容到 stderr
/// 决策: 复用 DebugLogRenderer.RenderAllAsync，避免与 /debuglog 命令代码重复
/// 决策: 输出到 stderr（TerminalHelper.WriteError），不污染 stdout，对脚本和 --json 模式友好
/// 决策: --json 模式下自动关闭，避免干扰结构化输出解析
/// 替代方案已否决: 新增独立 --debug 参数（增加认知负担，--debuglog 已是调试入口）
/// </summary>
[Register]
internal sealed partial class InitDebugDumpStep : ServiceEntity, IMiddleware<StartupContext>
{
    public async Task InvokeAsync(StartupContext context, MiddlewareDelegate<StartupContext> next, CancellationToken ct)
    {
        var options = context.Options;

        // 未启用 --debuglog → 跳过（正常用户无影响）
        // --json 模式 → 跳过（避免干扰结构化输出解析）
        if (!options.DebugLog || options.IsJsonMode)
        {
            await next(context, ct).ConfigureAwait(false);
            return;
        }

        // 渲染等价 /debuglog -a 的全部内容
        var content = await DebugLogRenderer.RenderAllAsync(context.Host.Services, ct).ConfigureAwait(false);

        // 输出到 stderr，不污染 stdout
        TerminalHelper.WriteError(content);
        Diag.WriteLine("[STEP] InitDebugDump: 已输出诊断信息到 stderr");

        await next(context, ct).ConfigureAwait(false);
    }
}
