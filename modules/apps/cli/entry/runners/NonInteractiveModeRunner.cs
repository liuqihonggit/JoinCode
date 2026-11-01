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

        // 结构化输出模式: JSON/NDJSON 非交互模式下抑制 stdout 信息输出
        if (options.IsStructuredOutputMode) {
            if (options.IsJsonMode) {
                var jsonContext = Cli.Output.CliOutputJsonContext.Default;
                context.OutputContract = new Cli.Output.CliOutputContract(jsonMode: true, jsonContext: jsonContext);
                Diag.WriteLine("[RUN] JSON output mode enabled (--json or --format json)");
            } else if (options.IsNdjsonMode) {
                Diag.WriteLine("[RUN] NDJSON stream mode enabled (--format ndjson)");
            }
            Cli.TerminalHelper.SuppressStdout = true; // Bug 12: 抑制 stdout 信息输出，确保只有结构化输出
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
        } else if (options.IsNdjsonMode) {
            // NDJSON 模式: 事件已由 CliEventConsumer 流式输出，此处仅输出最终 result 事件（含 exitCode 和 error）
            var resultEvent = new Cli.Output.CliStreamEvent("result") {
                Data = new Cli.Output.CliStreamEventData {
                    ExitCode = context.ExitCode,
                    Content = context.FullResponse,
                },
            };
            if (context.StructuredError is not null)
                resultEvent.Data.Error = context.StructuredError;
            var json = RelaxedJsonSerializer.Serialize(resultEvent, Cli.Output.CliOutputJsonContext.Default);
            Console.WriteLine(json);
        }

        Cli.TerminalHelper.SuppressStdout = false; // 恢复 stdout（测试可重入）
        return context.ExitCode;
    }
}