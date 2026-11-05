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
                // 流式: LLM 请求流式（jcc 默认用 StreamingFallbackDecorator 先尝试流式，失败降级非流式）
                // UseStreamingToolExecution 控制的是工具执行流式，非 LLM 流式，不在此显示
                Cli.TerminalHelper.WriteLine($"供应商: {p.Vendor} | 模型: {p.ModelId} | 流式: 是" +
                    (context.Config.CurrentProfile is not null ? $" | 预设: {context.Config.CurrentProfile}" : ""));
                Cli.TerminalHelper.WriteLine($"  端点: {p.Endpoint ?? "(默认)"} | API Key: {(string.IsNullOrEmpty(p.ApiKey) ? "未配置" : "已配置（未验证）")}");
            }
            var prompt = context.NonInteractivePrompt;
            if (string.IsNullOrEmpty(prompt)) {
                Diag.WriteLine("[STEP] ExecuteStep ERROR: NonInteractivePrompt is null/empty!");
                context.ExitCode = (int)ExitCode.GeneralError;
                return;
            }
            if (CliGuardConfig.MaxPromptLength != CliGuardConfig.Unset && prompt.Length > CliGuardConfig.MaxPromptLength) {
                Diag.WriteLine($"[STEP] ExecuteStep ERROR: prompt too long ({prompt.Length} > {CliGuardConfig.MaxPromptLength})");
                using (Cli.TerminalHelper.SetColor(ConsoleColor.Red)) {
                    Cli.TerminalHelper.WriteLine($"错误: 提示词过长 ({prompt.Length} 字符)，上限 {CliGuardConfig.MaxPromptLength} 字符。请缩短输入或通过文件传入。");
                }
                context.ExitCode = (int)ExitCode.GeneralError;
                return;
            }
            if (context.Options.DryRun) {
                Diag.WriteLine("[STEP] ExecuteStep dry-run mode, skipping LLM call");
                using (Cli.TerminalHelper.SetColor(ConsoleColor.Yellow)) {
                    Cli.TerminalHelper.WriteLine($"[试跑模式] 将发送提示词到 {p.Vendor}/{p.ModelId}:");
                    Cli.TerminalHelper.WriteLine($"  \"{prompt}\"");
                }
                context.ExitCode = 0;
                await next(context, ct).ConfigureAwait(false);
                return;
            }
            var sw = System.Diagnostics.Stopwatch.StartNew();
            await session.ProcessUserInputAsync(prompt, ct).ConfigureAwait(false);
            sw.Stop();
            context.ElapsedMs = sw.ElapsedMilliseconds;
            context.NumTurns = session.TotalTurns;
            context.TotalCostUsd = session.LastCostUsd;
            context.ApiDurationMs = session.LastApiDurationMs;
            context.StopReason = session.LastStopReason;
            if (session.PermissionDenials is { } denials)
                context.PermissionDenials.AddRange(denials);
            context.Usage = session.LastUsage is { } usage
                ? new Cli.Output.SdkUsage {
                    InputTokens = usage.PromptTokens,
                    OutputTokens = usage.CompletionTokens,
                    CacheCreationInputTokens = usage.CacheCreationInputTokens,
                    CacheReadInputTokens = usage.CacheReadInputTokens,
                }
                : null;
            await Console.Out.FlushAsync().ConfigureAwait(false);
            context.FullResponse = session.LastResponse;
            Diag.WriteLifecycle("[AI对话结束]");
            Diag.WriteLine("[STEP] ExecuteStep ProcessUserInputAsync returned, stdout flushed");
        } catch (OperationCanceledException) when (ct.IsCancellationRequested) {
            Diag.WriteLine("[STEP] ExecuteStep cancelled");
            context.ExitCode = (int)ExitCode.Interrupted;
            return;
        } catch (TimeoutException ex) {
            Diag.WriteLine($"[STEP] ExecuteStep timeout: {ex.Message}");
            context.StructuredError = new Cli.Output.CliStructuredError("LLM_TIMEOUT", ex.Message, null, true);
            if (context.OutputContract is null)
                Cli.TerminalHelper.WriteLine($"错误: 请求超时 — {ex.Message}");
            context.ExitCode = (int)ExitCode.LlmCallTimeout;
            return;
        } catch (Exception ex) {
            Diag.WriteLine($"[STEP] ExecuteStep exception: {ex.GetType().Name}: {ex.Message}");
            var errorLog = await WriteErrorLogAsync(ex).ConfigureAwait(false);
            var retryable = ex is JoinCode.Abstractions.Exceptions.ApiException apiEx && apiEx.IsRetryable;
            var (exitCode, errorCode) = ClassifyException(ex);
            context.StructuredError = new Cli.Output.CliStructuredError(errorCode, ex.Message, $"详细日志: {Cli.Output.XdgPathResolver.ShortenPath(errorLog)}", retryable);
            if (context.OutputContract is null) {
                Cli.TerminalHelper.WriteLine($"错误: {ex.Message}");
                if (retryable)
                    Cli.TerminalHelper.WriteLine("  此错误通常可重试，请稍后重试。");
                Cli.TerminalHelper.WriteLine("  请检查：1. API Key 配置  2. 网络连接  3. API 服务状态");
                Cli.TerminalHelper.WriteLine($"  详细日志: {Cli.Output.XdgPathResolver.ShortenPath(errorLog)}");
            }
            context.ExitCode = (int)exitCode;
            return;
        }

        Diag.WriteLine("[STEP] ExecuteStep done, calling next");
        Diag.WriteLifecycle("[EXIT]");
        await next(context, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 写入错误日志到 ~/.jcc/runtime/jcc_error.log（ADR 0055）
    /// </summary>
    private static async Task<string> WriteErrorLogAsync(Exception ex, ILogger? logger = null) {
        var errorLog = Cli.Output.XdgPathResolver.GetErrorLogPath();
        var errorContent = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}";
        try {
            await SafeFileIO.WriteAllText(errorLog, errorContent).ConfigureAwait(false);
        } catch (Exception logEx) {
            logger?.LogWarning(logEx, "写入错误日志失败");
        }
        return errorLog;
    }

    /// <summary>
    /// 将异常分类为退出码和错误代码 — 区分 LLM 调用失败、配置错误、工具执行失败和通用错误。
    /// <para>vv099 修复：外层异常未匹配时递归检查 InnerException 链 — HttpRequestException 被包为
    /// InvalidOperationException 后外层 Message 可能不含 IsLlmError 关键词，需递归发现内层类型。</para>
    /// </summary>
    internal static (ExitCode exitCode, string errorCode) ClassifyException(Exception ex) {
        var primary = ClassifyCore(ex);
        if (primary is not (ExitCode.GeneralError, "RUNTIME_ERROR")) return primary;

        var current = ex.InnerException;
        while (current is not null) {
            var inner = ClassifyCore(current);
            if (inner is not (ExitCode.GeneralError, "RUNTIME_ERROR")) return inner;
            current = current.InnerException;
        }

        return primary;
    }

    private static (ExitCode exitCode, string errorCode) ClassifyCore(Exception ex) => ex switch {
        JoinCode.Abstractions.Exceptions.ApiException => (ExitCode.LlmCallFailed, "LLM_CALL_FAILED"),
        System.Net.Http.HttpRequestException => (ExitCode.LlmCallFailed, "LLM_HTTP_ERROR"),
        JoinCode.Abstractions.Exceptions.ConfigurationException => (ExitCode.ConfigurationError, "CONFIG_ERROR"),
        JoinCode.Abstractions.Exceptions.CodeExecutionException => (ExitCode.ToolExecutionFailed, "TOOL_EXECUTION_FAILED"),
        System.NotSupportedException when ex.Message.Contains("scheme", StringComparison.OrdinalIgnoreCase) => (ExitCode.ConfigurationError, "CONFIG_ERROR"),
        System.InvalidOperationException when ex.Message.Contains("scheme", StringComparison.OrdinalIgnoreCase) => (ExitCode.ConfigurationError, "CONFIG_ERROR"),
        System.InvalidOperationException when IsLlmError(ex.Message) => (ExitCode.LlmCallFailed, "LLM_CALL_FAILED"),
        _ => (ExitCode.GeneralError, "RUNTIME_ERROR")
    };

    /// <summary>
    /// 判断异常消息是否表示 LLM 调用失败 — CliSession.StreamResponseAsync 把 LLM 错误包装成 InvalidOperationException。
    /// 覆盖：LLM 标识、HTTP 状态码、连接拒绝/网络错误（中英文）。
    /// </summary>
    internal static bool IsLlmError(string message) =>
        message.Contains("LLM", StringComparison.Ordinal) ||
        message.Contains("HTTP", StringComparison.Ordinal) ||
        message.Contains("连接", StringComparison.Ordinal) ||
        message.Contains("refused", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("unreachable", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("网络", StringComparison.Ordinal) ||
        message.Contains("network", StringComparison.OrdinalIgnoreCase);
}