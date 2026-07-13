namespace JoinCode.Entry;

/// <summary>
/// 启动日志中间件 — 记录每个启动步骤的耗时，统一捕获异常
/// 横切关注点示例：通过 Order = int.MinValue 排在最外层，包裹所有后续中间件
/// </summary>
[Register]
internal sealed class StartupLoggingMiddleware : IMiddleware<StartupContext>
{
    public ErrorBehavior OnError => ErrorBehavior.Propagate;

    public async Task InvokeAsync(StartupContext context, MiddlewareDelegate<StartupContext> next, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            await next(context, ct);
        }
        catch (OperationCanceledException)
        {
            // 用户取消，静默退出
            return;
        }
        catch (Exception ex)
        {
            sw.Stop();
            Cli.TerminalHelper.WriteLine($"[启动失败] {ex.GetType().Name}: {ex.Message} ({sw.ElapsedMilliseconds}ms)");
            context.ExitCode = 1;
            throw;
        }

        sw.Stop();

        // 诊断日志 — 受 JCC_VERBOSE 控制
        Diag.WriteLine($"[启动完成] 总耗时 {sw.ElapsedMilliseconds}ms");
    }
}
