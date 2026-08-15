namespace JoinCode.Entry;

using Infrastructure.Pipeline;

internal static class NonInteractiveModeRunner
{
    internal static async Task<int> RunAsync(WorkflowConfig config, CommandLineOptions options, IHost host)
    {
        Cli.TerminalHelper.Init();
        Diag.WriteLine("[RUN] NonInteractiveModeRunner entry");
        var context = new StartupContext
        {
            Config = config,
            Options = options,
            Host = host,
            FileSystem = IO.FileSystem.FileSystemFactory.Create()
        };

        // JSON 输出模式: 非交互模式下 --json 生效，注册 CliOutputContract 到上下文
        if (options.IsJsonMode)
        {
            var jsonContext = Cli.Output.CliOutputJsonContext.Default;
            context.OutputContract = new Cli.Output.CliOutputContract(jsonMode: true, jsonContext: jsonContext);
            Diag.WriteLine("[RUN] JSON output mode enabled (--json or --format json)");
        }

        var sp = host.Services;
        var pipeline = new PipelineBuilder<StartupContext>()
            .Use(sp.GetRequiredService<StartupLoggingMiddleware>())
            .Use(sp.GetRequiredService<NonInteractiveApiKeyCheckStep>())
            .Use(sp.GetRequiredService<SessionInitStep>())
            .Use(sp.GetRequiredService<SessionResumeStep>())
            .Use(sp.GetRequiredService<SystemPromptApplyStep>())
            .Use(sp.GetRequiredService<InitDebugDumpStep>())
            .Use(sp.GetRequiredService<NonInteractivePromptStep>())
            .Use(sp.GetRequiredService<NonInteractiveExecuteStep>())
            .Use(sp.GetRequiredService<NonInteractiveExitCleanupStep>())
            .OnError((ctx, ex) =>
            {
                Diag.WriteLine($"[RUN] OnError: {ex.GetType().Name}: {ex.Message}");
                if (ctx.OutputContract is not null)
                {
                    var error = new Cli.Output.CliStructuredError(
                        "RUNTIME_ERROR", ex.Message,
                        hint: "请检查错误日志获取详细信息",
                        retryable: false);
                    ctx.OutputContract.WriteError(error);
                }
                else
                {
                    Cli.TerminalHelper.WriteLine($"错误: {ex.Message}");
                }
                ctx.ExitCode = (int)ExitCode.GeneralError;
            })
            .Build();

        Diag.WriteLine("[RUN] pipeline built, executing...");
        await pipeline.ExecuteAsync(context, CancellationToken.None);
        Diag.WriteLine($"[RUN] pipeline done, exitCode={context.ExitCode}");

        // JSON 模式: 输出最终结果信封
        if (options.IsJsonMode && context.OutputContract is not null)
        {
            var result = new
            {
                exitCode = context.ExitCode,
                response = context.FullResponse ?? string.Empty,
            };
            context.OutputContract.WriteData(result, new Cli.Output.CliOutputMeta
            {
                DurationMs = context.ElapsedMs,
            });
        }

        return context.ExitCode;
    }
}
