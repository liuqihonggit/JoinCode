namespace JoinCode.Entry;

[Register(typeof(IMiddleware<StartupContext>), ServiceLifetime.Singleton)]
internal sealed partial class NonInteractiveExecuteStep : ServiceEntity, IMiddleware<StartupContext> {

    /// <summary>执行非交互模式用户输入处理中间件 — 将提示词送入会话处理并捕获超时/取消/通用异常，正常完成后传递给下一个中间件</summary>
    /// <param name="context">启动上下文，包含会话、配置与输出契约</param>
    /// <param name="next">下一个中间件委托</param>
    /// <param name="ct">取消令牌</param>
    public async Task InvokeAsync(StartupContext context, MiddlewareDelegate<StartupContext> next, CancellationToken ct) {
        Diag.WriteLine("[STEP] ExecuteStep start");
        var session = context.Session;
        if (session is null) {
            Diag.WriteLine("[STEP] ExecuteStep ERROR: context.Session is null!");
            context.ExitCode = (int)ExitCode.GeneralError;
            return;
        }
        Diag.WriteLine($"[STEP] ExecuteStep session={session.GetType().Name}");

        try {
            Diag.WriteLine("[STEP] ExecuteStep calling ProcessUserInputAsync...");
            Diag.WriteLifecycle("[AI助手] 开始处理");
            var p = context.Config.Provider;
            using (Cli.TerminalHelper.SetColor(ConsoleColor.DarkGray)) {
                Cli.TerminalHelper.WriteLine($"供应商: {p.Vendor} | 模型: {p.ModelId} | 流式: {(context.Config.ToolExecution.UseStreamingToolExecution ? "是" : "否")}" +
                    (context.Config.CurrentProfile is not null ? $" | 预设: {context.Config.CurrentProfile}" : ""));
                Cli.TerminalHelper.WriteLine($"  端点: {p.Endpoint ?? "(默认)"} | API Key: {(string.IsNullOrEmpty(p.ApiKey) ? "未配置" : "已配置（未验证）")}");
            }
            var prompt = context.NonInteractivePrompt;
            if (string.IsNullOrEmpty(prompt)) {
                Diag.WriteLine("[STEP] ExecuteStep ERROR: NonInteractivePrompt is null/empty!");
                context.ExitCode = (int)ExitCode.GeneralError;
                return;
            }
            await session.ProcessUserInputAsync(prompt, ct).ConfigureAwait(false);
            await Console.Out.FlushAsync().ConfigureAwait(false);
            Diag.WriteLifecycle("[AI对话结束]");
            Diag.WriteLine("[STEP] ExecuteStep ProcessUserInputAsync returned, stdout flushed");
        } catch (OperationCanceledException) when (ct.IsCancellationRequested) {
            Diag.WriteLine("[STEP] ExecuteStep cancelled");
            context.ExitCode = (int)ExitCode.Interrupted;
            return;
        } catch (TimeoutException ex) {
            Diag.WriteLine($"[STEP] ExecuteStep timeout: {ex.Message}");
            if (context.OutputContract is not null)
                context.OutputContract.WriteError(new Cli.Output.CliStructuredError("LLM_TIMEOUT", ex.Message, null, true));
            else
                Cli.TerminalHelper.WriteLine($"错误: 请求超时 — {ex.Message}");
            context.ExitCode = (int)ExitCode.LlmCallTimeout;
            return;
        } catch (Exception ex) {
            Diag.WriteLine($"[STEP] ExecuteStep exception: {ex.GetType().Name}: {ex.Message}");
            var errorLog = WriteErrorLog(ex);
            if (context.OutputContract is not null) {
                var retryable = ex is JoinCode.Abstractions.Exceptions.ApiException apiEx && apiEx.IsRetryable;
                context.OutputContract.WriteError(new Cli.Output.CliStructuredError("RUNTIME_ERROR", ex.Message, $"详细日志: {errorLog}", retryable));
            } else {
                Cli.TerminalHelper.WriteLine($"错误: {ex.Message}");
                if (ex is JoinCode.Abstractions.Exceptions.ApiException apiEx && apiEx.IsRetryable)
                    Cli.TerminalHelper.WriteLine("  此错误通常可重试，请稍后重试。");
                Cli.TerminalHelper.WriteLine($"  详细日志: {errorLog}");
            }
            context.ExitCode = (int)ExitCode.GeneralError;
            return;
        }

        Diag.WriteLine("[STEP] ExecuteStep done, calling next");
        Diag.WriteLifecycle("[EXIT]");
        await next(context, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 写入错误日志到 ~/.jcc/runtime/jcc_error.log（ADR 0055）
    /// </summary>
    private static string WriteErrorLog(Exception ex, ILogger? logger = null) {
        var errorLog = Cli.Output.XdgPathResolver.GetErrorLogPath();
        var errorContent = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}";
        try {
            SafeFileIO.WriteAllText(errorLog, errorContent).GetAwaiter().GetResult();
        } catch (Exception logEx) {
            logger?.LogWarning(logEx, "写入错误日志失败");
        }
        return errorLog;
    }
}