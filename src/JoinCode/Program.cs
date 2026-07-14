namespace JoinCode;

/// <summary>
/// 程序入口点 — 显式声明应用启动流程
/// </summary>
class Program
{
    static async Task<int> Main(string[] args)
    {
        Cli.TerminalHelper.Init();
        try
        {
            // 1. 本地化
            Infrastructure.Localization.LocalizerInitializer.Initialize(
                Environment.GetEnvironmentVariable(JccEnvVar.Language.ToValue()) ?? "zh");

            // 2. 子命令路由
            if (args.Length > 0 && App.Builder.ApplicationBuilder.IsSubCommand(args[0]))
                return await App.Builder.ApplicationBuilder.RunSubCommandAsync(args);

            // 3. 参数解析 → CommandLineOptions（后续全部使用 options，不再传递原始 args）
            var options = App.Builder.ApplicationBuilder.ParseArgs(args);
            if (options.ShowHelp) { App.Builder.ApplicationBuilder.ShowHelp(); return 0; }
            if (options.ShowVersion) { App.Builder.ApplicationBuilder.ShowVersion(); return 0; }

            // 3.5 --await N: 启动超时计时器，N秒后强制退出返回1234（用于诊断卡死）
            using var awaitTimer = StartAwaitTimer(options);

            var fs = IO.FileSystem.FileSystemFactory.Create();
            var config = await App.Builder.ApplicationBuilder.LoadConfigAsync(options, fs);

            var builder = new App.Builder.ApplicationBuilder()
                .UseModule<App.Modules.CoreModule>()
                .UseModule<App.Modules.ClockModule>()
                .UseModule<App.Modules.BrowserModule>()
                .UseModule<App.Modules.PipeModule>()
                .UseModule<App.Modules.CliModule>()
                .UseModule<App.Modules.McpInitModule>();

            var host = builder.BuildHost(config, options);

            await builder.ConfigureModulesAsync(host.Services);

            if (options.IsNonInteractiveMode)
                return await Entry.NonInteractiveModeRunner.RunAsync(config, options, host);

            await Entry.InteractiveModeRunner.RunAsync(config, options, host);
            return 0;
        }
        catch (Exception ex)
        {
            var errorLog = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "jcc_error.log");
            var errorContent = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}";
            System.IO.File.WriteAllText(errorLog, errorContent);

            Cli.TerminalHelper.Init();
            Cli.TerminalHelper.WriteLine();
            Cli.TerminalHelper.WriteLine($"发生错误: {ex.Message}");
            Cli.TerminalHelper.WriteLine();
            Cli.TerminalHelper.WriteLine($"详细日志: {errorLog}");

            return 1;
        }
    }

    /// <summary>
    /// 启动 --await N 超时计时器。
    /// N 秒后强制退出进程并返回 1234，用于诊断卡死问题。
    /// 正常完成时 using 释放计时器，不影响返回值。
    /// </summary>
    /// <remarks>
    /// ⚠️ Timer 回调中禁止在 <see cref="Environment.Exit(int)"/> 之前写 Console.Error：
    /// 当 stderr 被重定向到未读取的 pipe（如 PowerShell <c>Start-Process -RedirectStandardError</c>）时，
    /// Console.Error.WriteLine 会阻塞，导致 <see cref="Environment.Exit(int)"/> 永远不执行，
    /// 进程卡死。详见 <c>docs/AI交互文档/MockServer测试问题清单.md</c> P2-1。
    /// 启动时的日志 + ExitCode=1234 已足够诊断超时触发。
    /// </remarks>
    private static System.Threading.Timer? StartAwaitTimer(CommandLineOptions options)
    {
        if (options.AwaitTimeoutSeconds is not { } seconds || seconds <= 0)
            return null;

        Diag.WriteLine($"[MAIN] --await {seconds}s 计时器已启动（超时返回1234）");

        return new System.Threading.Timer(
            callback: _ =>
            {
                // ⚠️ 禁止在 Environment.Exit 之前写 Console.Error（详见方法 remarks 注释）
                Environment.Exit(1234);
            },
            state: null,
            dueTime: TimeSpan.FromSeconds(seconds),
            period: System.Threading.Timeout.InfiniteTimeSpan);
    }
}
