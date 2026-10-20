namespace JoinCode.App.Builder;

/// <summary>
/// CLI 会话工厂 — CLI 宿主专属会话组装（ADR 0143 Step 3b 从 EngineSessionFactory 拆出）。
/// <para>引擎通用路径在引擎侧 EngineSessionFactory.CreateSessionAsync；此处补充 CLI 模块</para>
/// <para>（CliRegistration/Cli/Pipe）与终端日志 provider（ConsoleActor 串行化，ADR 0100）。</para>
/// </summary>
public static class CliSessionFactory {
    /// <summary>
    /// 创建 CLI 引擎会话 — 含全部 CLI 模块（CliRegistration/Cli/Pipe + 引擎通用五模块）。
    /// CLI 的 Program.Main/McpCommand/BridgeServeCommand 调用此方法。
    /// </summary>
    public static Task<EngineSessionFactory.Result> CreateCliSessionAsync(
        CommandLineOptions options, IFileSystem fs, CancellationToken cancellationToken = default) {
        // ADR 0100: ConsoleActor 日志走 Actor 串行化；无 Actor 时引擎回退默认 console
        var consoleActor = Cli.TerminalHelper.GetConsoleActor();
        ILoggerProvider? loggerProvider = consoleActor is not null
            ? new Cli.Display.ConsoleActorLoggerProvider(consoleActor)
            : null;

        return EngineSessionFactory.CreateSessionAsync(
            options, fs,
            [
                new Modules.CoreModule(),
                new Modules.CliRegistrationModule(),
                new Modules.ClockModule(),
                new Modules.BrowserModule(),
                new Modules.PipeModule(),
                new Modules.CliModule(),
                new Modules.HousekeepingModule(),
                new Modules.McpInitModule(),
            ],
            loggerProvider,
            cancellationToken);
    }
}
