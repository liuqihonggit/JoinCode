namespace JoinCode.Entry;


internal static class NonInteractiveModeRunner {
    internal static async Task<int> RunAsync(WorkflowConfig config, CommandLineOptions options, IHost host) {
        Cli.TerminalHelper.Init();
        Diag.WriteLine("[RUN] NonInteractiveModeRunner entry");
        var context = new StartupContext {
            Config = config,
            Options = options,
            Host = host,
            FileSystem = IO.FileSystem.FileSystemFactory.Create()
        };

        // JSON 输出模式: 非交互模式下 --json 生效，注册 CliOutputContract 到上下文
        if (options.IsJsonMode) {
            var jsonContext = Cli.Output.CliOutputJsonContext.Default;
            context.OutputContract = new Cli.Output.CliOutputContract(jsonMode: true, jsonContext: jsonContext);
            Cli.TerminalHelper.SuppressStdout = true; // Bug 12: 抑制 stdout 信息输出，确保只有 JSON 信封
            Diag.WriteLine("[RUN] JSON output mode enabled (--json or --format json)");
        }

        var sp = host.Services;
        var pipeline = new PipelineBuilder<StartupContext>()
            .Use(sp.GetRequiredService<StartupLoggingMiddleware>())
            .Use(sp.GetRequiredService<NonInteractiveApiKeyCheckStep>())
            .Use(sp.GetRequiredService<DebugDumpPromptStep>())
            .Use(sp.GetRequiredService<SessionInitStep>())
            .Use(sp.GetRequiredService<SessionResumeStep>())
            .Use(sp.GetRequiredService<SystemPromptApplyStep>())
            .Use(sp.GetRequiredService<InitDebugDumpStep>())
            .Use(sp.GetRequiredService<NonInteractivePromptStep>())
            .Use(sp.GetRequiredService<NonInteractiveExecuteStep>())
            .Use(sp.GetRequiredService<NonInteractiveExitCleanupStep>())
            .OnError((ctx, ex) => {
                Diag.WriteLine($"[RUN] OnError: {ex.GetType().Name}: {ex.Message}");
                ctx.StructuredError = new Cli.Output.CliStructuredError(
                    "RUNTIME_ERROR", ex.Message,
                    hint: "请检查错误日志获取详细信息",
                    retryable: false);
                ctx.ExitCode = (int)ExitCode.GeneralError;
            })
            .Build();

        Diag.WriteLine("[RUN] pipeline built, executing...");
        await pipeline.ExecuteAsync(context, CancellationToken.None).ConfigureAwait(false);
        Diag.WriteLine($"[RUN] pipeline done, exitCode={context.ExitCode}");

        // JSON 模式: 输出统一结果信封 — 同时包含 data 和 error（Bug 16: 统一 JSON 信封返回格式）
        if (options.IsJsonMode && context.OutputContract is not null) {
            var result = new Cli.Output.CliNonInteractiveResult(context.ExitCode, context.FullResponse ?? string.Empty);
            var ok = context.ExitCode == 0;
            var error = ok ? null : (context.StructuredError ?? ((ExitCode)context.ExitCode).ToStructuredError());
            context.OutputContract.WriteResult(result, ok, error, new Cli.Output.CliOutputMeta {
                DurationMs = context.ElapsedMs,
            });
        }

        Cli.TerminalHelper.SuppressStdout = false; // 恢复 stdout（测试可重入）
        return context.ExitCode;
    }
}