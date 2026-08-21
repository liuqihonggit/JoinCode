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
            context.ExitCode = (int)ExitCode.GeneralError;
            return;
        }
        Diag.WriteLine($"[STEP] ExecuteStep session={session.GetType().Name}");

        try
        {
        Diag.WriteLine("[STEP] ExecuteStep calling ProcessUserInputAsync...");
        Diag.WriteLifecycle("[AI助手] 开始处理");
        var p = context.Config.Provider;
        using (Cli.TerminalHelper.SetColor(ConsoleColor.DarkGray))
        {
            Cli.TerminalHelper.WriteLine($"供应商: {p.Vendor} | 模型: {p.ModelId} | 流式: {(context.Config.ToolExecution.UseStreamingToolExecution ? "是" : "否")}" +
                (context.Config.CurrentProfile is not null ? $" | 预设: {context.Config.CurrentProfile}" : ""));
            Cli.TerminalHelper.WriteLine($"  端点: {p.Endpoint ?? "(默认)"} | API Key: {(string.IsNullOrEmpty(p.ApiKey) ? "未配置" : "已配置（未验证）")}");
        }
        var prompt = context.NonInteractivePrompt;
            if (string.IsNullOrEmpty(prompt))
            {
                Diag.WriteLine("[STEP] ExecuteStep ERROR: NonInteractivePrompt is null/empty!");
                context.ExitCode = (int)ExitCode.GeneralError;
                return;
            }
            await session.ProcessUserInputAsync(prompt, ct);
            await Console.Out.FlushAsync().ConfigureAwait(false);
            Diag.WriteLifecycle("[AI对话结束]");
            Diag.WriteLine("[STEP] ExecuteStep ProcessUserInputAsync returned, stdout flushed");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            Diag.WriteLine("[STEP] ExecuteStep cancelled");
            context.ExitCode = (int)ExitCode.Interrupted;
            return;
        }
        catch (TimeoutException ex)
        {
            Diag.WriteLine($"[STEP] ExecuteStep timeout: {ex.Message}");
            Cli.TerminalHelper.WriteLine($"错误: 请求超时 — {ex.Message}");
            context.ExitCode = (int)ExitCode.LlmCallTimeout;
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
            context.ExitCode = (int)ExitCode.GeneralError;
            return;
        }

        Diag.WriteLine("[STEP] ExecuteStep done, calling next");
        Diag.WriteLifecycle("[EXIT]");
        await next(context, ct);
    }

    /// <summary>
    /// 写入错误日志到临时目录的 jcc_error.log
    /// </summary>
    private static string WriteErrorLog(Exception ex, ILogger? logger = null)
    {
        var errorLog = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "jcc_error.log");
        var errorContent = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}";
        try
        {
            SafeFileIO.WriteAllText(errorLog, errorContent);
        }
        catch (Exception logEx)
        {
            logger?.LogWarning(logEx, "写入错误日志失败");
        }
        return errorLog;
    }
}
