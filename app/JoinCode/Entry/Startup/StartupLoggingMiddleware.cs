namespace JoinCode.Entry;

/// <summary>
/// 启动日志中间件 — 记录每个启动步骤的耗时，统一捕获异常
/// 横切关注点示例：通过 Order = int.MinValue 排在最外层，包裹所有后续中间件
/// </summary>
[Register]
internal sealed partial class StartupLoggingMiddleware : ServiceEntity, IMiddleware<StartupContext>
{

    public async Task InvokeAsync(StartupContext context, MiddlewareDelegate<StartupContext> next, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            await next(context, ct);
        }
        catch (OperationCanceledException)
        {
            // 用户取消 — 设置中断退出码，避免误报为成功（对齐 Program.cs 的 130 = 128+SIGINT）
            context.ExitCode = (int)ExitCode.Interrupted;
            return;
        }
        catch (Exception ex)
        {
            sw.Stop();
            Cli.TerminalHelper.WriteLine($"[启动失败] {ex.GetType().Name}: {ex.Message} ({sw.ElapsedMilliseconds}ms)");
            context.ExitCode = (int)ExitCode.GeneralError;
            throw;
        }

        sw.Stop();

        // 诊断日志 — 受 JCC_VERBOSE 控制
        Diag.WriteLine($"[启动完成] 总耗时 {sw.ElapsedMilliseconds}ms");
    }
}
