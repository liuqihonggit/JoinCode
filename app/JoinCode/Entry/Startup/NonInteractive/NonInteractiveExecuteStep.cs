namespace JoinCode.Entry;

[Register]
internal sealed partial class NonInteractiveExecuteStep : ServiceEntity, IMiddleware<StartupContext>
{

    public async Task InvokeAsync(StartupContext context, MiddlewareDelegate<StartupContext> next, CancellationToken ct)
    {
        Diag.WriteLine("[STEP] ExecuteStep start");
        var session = context.Session;
        if (session is null)
        {
            Diag.WriteLine("[STEP] ExecuteStep ERROR: context.Session is null!");
            context.ExitCode = 1;
            return;
        }
        Diag.WriteLine($"[STEP] ExecuteStep session={session.GetType().Name}");

        try
        {
        Diag.WriteLine("[STEP] ExecuteStep calling ProcessUserInputAsync...");
        Diag.WriteLifecycle("[READY]");
        var prompt = context.NonInteractivePrompt;
            if (string.IsNullOrEmpty(prompt))
            {
                Diag.WriteLine("[STEP] ExecuteStep ERROR: NonInteractivePrompt is null/empty!");
                context.ExitCode = 1;
                return;
            }
            await session.ProcessUserInputAsync(prompt, ct);
            await Console.Out.FlushAsync().ConfigureAwait(false);
            Diag.WriteLifecycle("[DONE]");
            Diag.WriteLine("[STEP] ExecuteStep ProcessUserInputAsync returned, stdout flushed");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            Diag.WriteLine("[STEP] ExecuteStep cancelled");
            context.ExitCode = 130;
            return;
        }
        catch (Exception ex)
        {
            Diag.WriteLine($"[STEP] ExecuteStep exception: {ex.GetType().Name}: {ex.Message}");
            var errorLog = WriteErrorLog(ex);
            Cli.TerminalHelper.WriteLine($"错误: {ex.Message}");
            if (ex is JoinCode.Abstractions.Exceptions.ApiException apiEx && apiEx.IsRetryable)
                Cli.TerminalHelper.WriteLine("  此错误通常可重试，请稍后重试。");
            Cli.TerminalHelper.WriteLine($"  详细日志: {errorLog}");
            context.ExitCode = 1;
            return;
        }

        Diag.WriteLine("[STEP] ExecuteStep done, calling next");
        Diag.WriteLifecycle("[EXIT]");
        await next(context, ct);
    }

    /// <summary>
    /// 写入错误日志到临时目录的 jcc_error.log
    /// </summary>
    private static string WriteErrorLog(Exception ex)
    {
        var errorLog = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "jcc_error.log");
        var errorContent = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}";
        try
        {
            System.IO.File.WriteAllText(errorLog, errorContent);
        }
        catch (Exception logEx)
        {
            System.Diagnostics.Trace.WriteLine($"写入错误日志失败: {logEx.Message}");
        }
        return errorLog;
    }
}
