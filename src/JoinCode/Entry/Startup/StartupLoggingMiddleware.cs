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

        // 仅在 JCC_VERBOSE 环境变量设置时输出耗时
        if (Environment.GetEnvironmentVariable("JCC_VERBOSE") is not null)
        {
            Cli.TerminalHelper.WriteLine($"[启动完成] 总耗时 {sw.ElapsedMilliseconds}ms");
        }
    }
}
