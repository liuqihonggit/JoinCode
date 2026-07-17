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

            // 3.1 --doctor: 医生模式 — spawn jcc.exe 子进程作为病人，监控运行状态并自动修复问题
            if (options.DoctorMode)
                return await Entry.DoctorModeRunner.RunAsync(options);

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
        catch (OperationCanceledException)
        {
            // P2-7: 用户取消（Ctrl+C）或网络请求取消 — 静默退出，不写 error.log（非程序 bug）
            // 退出码 130 = POSIX 标准（128 + SIGINT=2），便于 shell 脚本区分中断与正常错误
            return 130;
        }
        catch (ConfigurationException ex)
        {
            // P2-7: 配置问题 — 友好提示，不写入 error.log（非程序 bug，用户可自行修复）
            // 退出码 2 = 配置错误专用，便于 CI/脚本区分配置问题与运行时错误
            // 视角2 #27: 使用 ErrorConsole.Warning 渲染（黄色警告 + 图标）
            Cli.TerminalHelper.Init();
            App.ErrorConsole.Warning(ex.Message);
            if (!string.IsNullOrEmpty(ex.ConfigurationKey))
                Cli.TerminalHelper.WriteError($"  配置项: {ex.ConfigurationKey}");
            if (!string.IsNullOrEmpty(ex.ConfigurationFilePath))
                Cli.TerminalHelper.WriteError($"  配置文件: {ex.ConfigurationFilePath}");
            Cli.TerminalHelper.WriteError("  请检查配置文件或环境变量后重试。");
            return 2;
        }
        catch (Exception ex) when (ex is OutOfMemoryException or TypeInitializationException)
        {
            // P2-7: 不可恢复异常 — 记录日志后 rethrow 让进程崩溃（继续运行可能损坏数据）
            WriteErrorLog(ex, fatal: true);
            throw;
        }
        catch (Exception ex)
        {
            // 通用异常 — 记录日志并友好提示
            // 视角2 #27: 使用 ErrorConsole.Fatal 渲染（红色致命错误 + 图标）
            var errorLog = WriteErrorLog(ex);

            Cli.TerminalHelper.Init();
            App.ErrorConsole.Fatal(ex.Message);
            Cli.TerminalHelper.WriteError($"  详细日志: {errorLog}");

            return 1;
        }
    }

    /// <summary>
    /// 写入错误日志到临时目录的 jcc_error.log。
    /// </summary>
    /// <param name="ex">异常对象</param>
    /// <param name="fatal">是否为致命异常（标记 [FATAL] 前缀）</param>
    /// <returns>错误日志文件路径</returns>
    private static string WriteErrorLog(Exception ex, bool fatal = false)
    {
        var errorLog = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "jcc_error.log");
        var prefix = fatal ? "[FATAL] " : string.Empty;
        var errorContent = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {prefix}{ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}";
        try
        {
            System.IO.File.WriteAllText(errorLog, errorContent);
        }
        catch (Exception logEx)
        {
            // 写入日志失败不应影响主流程 — 记录到跟踪监听器
            System.Diagnostics.Trace.WriteLine($"写入错误日志失败: {logEx.Message}");
        }
        return errorLog;
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
