namespace JoinCode.Entry;

using Infrastructure.Pipeline;

internal static class InteractiveModeRunner
{
    internal static async Task RunAsync(WorkflowConfig config, CommandLineOptions options, IHost host, CancellationToken cancellationToken = default)
    {
        Cli.TerminalHelper.Init();
        Cli.TerminalHelper.WriteLine("JoinCode - AI 智能体命令行工具");
        Cli.TerminalHelper.NewLine();

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
            .Use(sp.GetRequiredService<ProviderSetupStep>())
            .Use(sp.GetRequiredService<WorkspaceTrustStep>())
            .Use(sp.GetRequiredService<SessionInitStep>())
            .Use(sp.GetRequiredService<SessionResumeStep>())
            .Use(sp.GetRequiredService<SystemPromptApplyStep>())
            .Use(sp.GetRequiredService<ReplLoopStep>())
            .Use(sp.GetRequiredService<ExitCleanupStep>())
            .OnError((ctx, ex) =>
            {
                Cli.TerminalHelper.WriteLine($"启动失败: {ex.Message}");
            })
            .Build();

        await pipeline.ExecuteAsync(context, cancellationToken);
    }
}
