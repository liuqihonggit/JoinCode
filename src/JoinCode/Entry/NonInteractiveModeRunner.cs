namespace JoinCode.Entry;

using Infrastructure.Pipeline;

internal static class NonInteractiveModeRunner
{
    internal static async Task<int> RunAsync(WorkflowConfig config, CommandLineOptions options, IHost host)
    {
        Cli.TerminalHelper.Init();
        Console.Error.WriteLine("[RUN] NonInteractiveModeRunner entry");
        var context = new StartupContext
        {
            Config = config,
            Options = options,
            Host = host,
            FileSystem = IO.FileSystem.FileSystemFactory.Create()
        };

        var sp = host.Services;
        var pipeline = new PipelineBuilder<StartupContext>()
            .Use(sp.GetRequiredService<StartupLoggingMiddleware>())
            .Use(sp.GetRequiredService<NonInteractiveApiKeyCheckStep>())
            .Use(sp.GetRequiredService<SessionInitStep>())
            .Use(sp.GetRequiredService<NonInteractivePromptStep>())
            .Use(sp.GetRequiredService<NonInteractiveExecuteStep>())
            .Use(sp.GetRequiredService<NonInteractiveExitCleanupStep>())
            .OnError((ctx, ex) =>
            {
                Console.Error.WriteLine($"[RUN] OnError: {ex.GetType().Name}: {ex.Message}");
                Cli.TerminalHelper.WriteLine($"错误: {ex.Message}");
                ctx.ExitCode = 1;
            })
            .Build();

        Console.Error.WriteLine("[RUN] pipeline built, executing...");
        await pipeline.ExecuteAsync(context, CancellationToken.None);
        Console.Error.WriteLine($"[RUN] pipeline done, exitCode={context.ExitCode}");
        return context.ExitCode;
    }
}
