namespace JoinCode;

/// <summary>
/// 程序入口点 — 显式声明应用启动流程
/// </summary>
class Program
{
    static async Task<int> Main(string[] args)
    {
        // 密钥红线检查 — 禁止在命令行参数中传递 API Key
        var secretWarning = Cli.Output.ApiKeyRedLine.CheckArgsForSecrets(args);
        if (secretWarning is not null)
        {
            Cli.TerminalHelper.Init();
            App.ErrorConsole.Warning(secretWarning);
            return (int)ExitCode.ArgumentParseError;
        }

        InstallGlobalExceptionHandlers();

        Cli.TerminalHelper.Init();
        JoinCode.Abstractions.Shell.CommandTerminal.SetConsole(new CliCommandConsole());
        ILogger<Program>? logger = null;
        CommandLineOptions? options = null;
        try
        {
            // 1. 本地化
            Infrastructure.Localization.LocalizerInitializer.Initialize(
                Environment.GetEnvironmentVariable(JccEnvVar.Language.ToValue()) ?? "zh");

            // 2. 子命令路由
            if (args.Length > 0 && App.Builder.ApplicationBuilder.IsSubCommand(args[0]))
                return await App.Builder.ApplicationBuilder.RunSubCommandAsync(args);

            // 3. 参数解析 → CommandLineOptions（后续全部使用 options，不再传递原始 args）
            options = App.Builder.ApplicationBuilder.ParseArgs(args);
            if (options.ShowHelp) { App.Builder.ApplicationBuilder.ShowHelp(); return 0; }
            if (options.ShowVersion) { App.Builder.ApplicationBuilder.ShowVersion(); return 0; }

            // 3.1 --doctor: 医生模式 — spawn jcc.exe 子进程作为病人，监控运行状态并自动修复问题
            // 需要先构建 DI 容器以解析 IChatClient + IQueryService（LLM 服务）
            if (options.DoctorMode)
            {
                var doctorFs = IO.FileSystem.FileSystemFactory.Create();
                var doctorResult = await App.Builder.EngineSessionFactory.CreateCliSessionAsync(options, doctorFs);

                try
                {
                    return await Entry.DoctorModeRunner.RunAsync(options, doctorResult.Host.Services);
                }
                finally
                {
                    if (doctorResult.Host is IAsyncDisposable asyncDoc)
                        await asyncDoc.DisposeAsync();
                    else
                        doctorResult.Host.Dispose();
                }
            }

            // 3.2 --doctor-endpoint: 病人模式 — 连接到医生的 SSE 服务器，发送遥测事件
            // 病人正常运行，但额外启动 DoctorSseClient 把诊断输出推送给医生
            Core.Agents.Doctor.DoctorSseClient? doctorClient = null;
            if (options.DoctorEndpoint is not null)
            {
                doctorClient = new Core.Agents.Doctor.DoctorSseClient(options.DoctorEndpoint);
                await doctorClient.ConnectAsync();

                Diag.DiagnosticLineWritten += async (_, line) =>
                {
                    try { await doctorClient.SendTextEventAsync("diag_output", line).ConfigureAwait(false); }
                    catch (Exception ex) { logger?.LogWarning(ex, "[Doctor] 发送遥测失败"); }
                };

                doctorClient.CommandReceived += (_, command) =>
                {
                    Diag.WriteLifecycle($"[Doctor] 收到医生指令: {command}");
                };

                Diag.WriteLine($"[MAIN] Doctor SSE 客户端已连接: {options.DoctorEndpoint}");
            }

            // 3.5 --await N: 启动超时计时器，N秒后强制退出返回 ExitCode.AwaitTimeout（用于诊断卡死）
            using var awaitTimer = StartAwaitTimer(options, logger);

            var fs = IO.FileSystem.FileSystemFactory.Create();

            var engineResult = await App.Builder.EngineSessionFactory.CreateCliSessionAsync(options, fs);

            var config = engineResult.Config;
            var host = engineResult.Host;

            logger = host.Services.GetService<ILogger<Program>>();

            // 3.3 工具执行遥测：订阅 PermissionAwareToolExecutor.ToolExecutionCompleted，转发给医生
            if (doctorClient is not null)
            {
                var toolExecutor = host.Services.GetService<McpToolRegistry.PermissionAwareToolExecutor>();
                if (toolExecutor is not null)
                {
                    toolExecutor.ToolExecutionCompleted += async (_, e) =>
                    {
                        try
                        {
                            var eventType = e.IsError ? "tool_error" : "tool_success";
                            var data = $"{{\"tool\":\"{e.ToolName}\",\"isError\":{e.IsError.ToString().ToLowerInvariant()}}}";
                            await doctorClient.SendTextEventAsync(eventType, data).ConfigureAwait(false);
                        }
                        catch (Exception ex) { logger?.LogWarning(ex, "[Doctor] 发送工具遥测失败"); }
                    };
                }
            }

            int exitCode;
            if (options.IsNonInteractiveMode)
                exitCode = await Entry.NonInteractiveModeRunner.RunAsync(config, options, host);
            else
            {
                await Entry.InteractiveModeRunner.RunAsync(config, options, host);
                exitCode = 0;
            }

            if (doctorClient is not null)
                await doctorClient.DisposeAsync().ConfigureAwait(false);

            return exitCode;
        }
        catch (OperationCanceledException)
        {
            // P2-7: 用户取消（Ctrl+C）或网络请求取消 — 静默退出，不写 error.log（非程序 bug）
            // 退出码 130 = POSIX 标准（128 + SIGINT=2），便于 shell 脚本区分中断与正常错误
            return (int)ExitCode.Interrupted;
        }
        catch (TimeoutException ex)
        {
            // 超时兜底 — NonInteractiveExecuteStep 已先捕获，此处处理管道其他步骤的超时
            Cli.TerminalHelper.Init();
            if (options?.IsJsonMode == true)
            {
                WriteJsonError(Cli.Output.CliErrorCatalog.NetTimeout(ex.Message));
            }
            else
            {
                App.ErrorConsole.Warning($"请求超时: {ex.Message}");
            }
            return (int)ExitCode.LlmCallTimeout;
        }
        catch (ConfigurationException ex)
        {
            // P2-7: 配置问题 — 友好提示，不写入 error.log（非程序 bug，用户可自行修复）
            // 退出码 2 = 配置错误专用，便于 CI/脚本区分配置问题与运行时错误
            Cli.TerminalHelper.Init();
            if (options?.IsJsonMode == true)
            {
                var error = Cli.Output.CliErrorCatalog.ConfigInvalidValue(
                    ex.ConfigurationKey ?? "unknown",
                    ex.Message,
                    "请检查配置文件或环境变量后重试");
                WriteJsonError(error);
            }
            else
            {
                App.ErrorConsole.Warning(ex.Message);
                if (!string.IsNullOrEmpty(ex.ConfigurationKey))
                    Cli.TerminalHelper.WriteError($"  配置项: {ex.ConfigurationKey}");
                if (!string.IsNullOrEmpty(ex.ConfigurationFilePath))
                    Cli.TerminalHelper.WriteError($"  配置文件: {ex.ConfigurationFilePath}");
                Cli.TerminalHelper.WriteError("  请检查配置文件或环境变量后重试。");
            }
            return (int)ExitCode.ConfigurationError;
        }
        catch (Exception ex) when (ex is OutOfMemoryException or TypeInitializationException)
        {
            // P2-7: 不可恢复异常 — 记录日志后 rethrow 让进程崩溃（继续运行可能损坏数据）
            WriteErrorLog(ex, fatal: true, logger);
            throw;
        }
        catch (Exception ex)
        {
            // 通用异常 — 记录日志并友好提示
            // 视角2 #27: 使用 ErrorConsole.Fatal 渲染（红色致命错误 + 图标）
            var errorLog = WriteErrorLog(ex, logger: logger);

            Cli.TerminalHelper.Init();
            if (options?.IsJsonMode == true)
            {
                WriteJsonError(new Cli.Output.CliStructuredError(
                    "RUNTIME_ERROR", ex.Message,
                    hint: $"详细日志: {errorLog}",
                    retryable: false));
            }
            else
            {
                App.ErrorConsole.Fatal(ex.Message);
                Cli.TerminalHelper.WriteError($"  详细日志: {errorLog}");
            }

            return (int)ExitCode.GeneralError;
        }
    }

    /// <summary>
    /// 写入错误日志到临时目录的 jcc_error.log。
    /// </summary>
    /// <param name="ex">异常对象</param>
    /// <param name="fatal">是否为致命异常（标记 [FATAL] 前缀）</param>
    /// <returns>错误日志文件路径</returns>
    private static string WriteErrorLog(Exception ex, bool fatal = false, ILogger? logger = null)
    {
        var errorLog = Cli.Output.XdgPathResolver.GetErrorLogPath();
        var prefix = fatal ? "[FATAL] " : string.Empty;
        var errorContent = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {prefix}{ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}";
        try
        {
            SafeFileIO.WriteAllText(errorLog, errorContent);
        }
        catch (Exception logEx)
        {
            logger?.LogWarning(logEx, "写入错误日志失败");
        }
        return errorLog;
    }

    /// <summary>
    /// 写入结构化 JSON 错误到 stderr — 供 JSON 模式下的异常捕获链使用
    /// </summary>
    private static void WriteJsonError(Cli.Output.CliStructuredError error)
    {
        var contract = new Cli.Output.CliOutputContract(
            jsonMode: true,
            jsonContext: Cli.Output.CliOutputJsonContext.Default);
        contract.WriteError(error);
    }

    /// <summary>
    /// 启动 --await N 超时计时器。
    /// N 秒后强制退出进程并返回 ExitCode.AwaitTimeout (=1234)，用于诊断卡死问题。
    /// 正常完成时 using 释放计时器，不影响返回值。
    /// </summary>
    /// <remarks>
    /// ⚠️ Timer 回调中禁止在 <see cref="Environment.Exit(int)"/> 之前写 Console.Error：
    /// 当 stderr 被重定向到未读取的 pipe（如 PowerShell <c>Start-Process -RedirectStandardError</c>）时，
    /// Console.Error.WriteLine 会阻塞，导致 <see cref="Environment.Exit(int)"/> 永远不执行，
    /// 进程卡死。详见 <c>docs/AI交互文档/MockServer测试问题清单.md</c> P2-1。
    /// 启动时的日志 + ExitCode=AwaitTimeout 已足够诊断超时触发。
    /// </remarks>
    private static System.Threading.Timer? StartAwaitTimer(CommandLineOptions options, ILogger? logger = null)
    {
        if (options.AwaitTimeoutSeconds is not { } seconds || seconds <= 0)
            return null;

        Diag.WriteLine($"[MAIN] --await {seconds}s 计时器已启动（超时返回{(int)ExitCode.AwaitTimeout}）");

        return new System.Threading.Timer(
            callback: _ =>
            {
                // ⚠️ 禁止在 Environment.Exit 之前写 Console.Error（详见方法 remarks 注释）
                // 超时诊断降级：写时间戳文件留审计轨迹（文件写不依赖 Console，不会因 pipe 阻塞）
                try
                {
                    var timeoutLog = Cli.Output.XdgPathResolver.GetAwaitTimeoutLogPath();
                    SafeFileIO.AppendAllText(timeoutLog,
                        $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] --await {seconds}s 超时, 进程强制退出({(int)ExitCode.AwaitTimeout})\n");
                }
                catch (Exception logEx)
                {
                    // 诊断日志失败不影响超时退出
                    logger?.LogWarning(logEx, "写入 --await 超时日志失败");
                }

                Environment.Exit((int)ExitCode.AwaitTimeout);
            },
            state: null,
            dueTime: TimeSpan.FromSeconds(seconds),
            period: System.Threading.Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    /// 安装全局异常钩子 — 捕获未处理异常和未观察的 Task 异常，写入崩溃快照和结构化日志
    /// </summary>
    private static void InstallGlobalExceptionHandlers()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var ex = e.ExceptionObject as Exception ?? new Exception("未知异常");
            WriteCrashDump(ex, source: "UnhandledException");
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            WriteCrashDump(e.Exception, source: "UnobservedTaskException");
            e.SetObserved();
        };
    }

    /// <summary>
    /// 写入崩溃快照到临时目录 — 结构化 JSON + 人类可读文本
    /// 路径由 XdgPathResolver.GetCrashDumpsDirectory() 统一管理
    /// </summary>
    private static void WriteCrashDump(Exception exception, string source)
    {
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var dumpDir = Cli.Output.XdgPathResolver.GetCrashDumpsDirectory();
        try { System.IO.Directory.CreateDirectory(dumpDir); } catch (Exception dirEx) { Diag.WriteError("[CrashDump] 创建目录失败", dirEx); }

        var snapshot = new CrashSnapshot(
            fenceName: $"Global.{source}",
            severity: CrashSeverity.Fatal,
            exception: exception,
            executionContext: new CrashExecutionContext
            {
                OperationName = source,
                Extra = { ["processId"] = Environment.ProcessId.ToString() }
            });

        // 1. 结构化 JSON 快照（AOT 安全 — 手动拼接 JSON 字符串）
        try
        {
            var jsonPath = System.IO.Path.Combine(dumpDir, $"crash_{timestamp}_{snapshot.Id:N8}.json");
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"id\": \"{snapshot.Id}\",");
            sb.AppendLine($"  \"capturedAt\": \"{snapshot.CapturedAt:O}\",");
            sb.AppendLine($"  \"fenceName\": \"{EscapeJson(snapshot.FenceName)}\",");
            sb.AppendLine($"  \"severity\": \"{snapshot.Severity.ToValue()}\",");
            sb.AppendLine($"  \"exceptionType\": \"{EscapeJson(snapshot.ExceptionType)}\",");
            sb.AppendLine($"  \"exceptionMessage\": \"{EscapeJson(snapshot.ExceptionMessage)}\",");
            sb.AppendLine($"  \"errorCode\": \"{EscapeJson(snapshot.ErrorCode)}\",");
            sb.AppendLine($"  \"stackTrace\": \"{EscapeJson(snapshot.StackTrace)}\",");
            sb.AppendLine($"  \"source\": \"{EscapeJson(source)}\",");
            sb.AppendLine("  \"exceptionChain\": [");
            for (var i = 0; i < snapshot.ExceptionChain.Frames.Length; i++)
            {
                var f = snapshot.ExceptionChain.Frames[i];
                sb.AppendLine("    {");
                sb.AppendLine($"      \"depth\": {f.Depth},");
                sb.AppendLine($"      \"type\": \"{EscapeJson(f.ExceptionType)}\",");
                sb.AppendLine($"      \"message\": \"{EscapeJson(f.Message)}\",");
                sb.AppendLine($"      \"errorCode\": \"{EscapeJson(f.ErrorCode)}\"");
                sb.Append("    }");
                if (i < snapshot.ExceptionChain.Frames.Length - 1) sb.AppendLine(",");
                else sb.AppendLine();
            }
            sb.AppendLine("  ],");
            sb.AppendLine("  \"executionContext\": {");
            sb.AppendLine($"    \"operationName\": \"{EscapeJson(snapshot.ExecutionContext.OperationName)}\",");
            sb.AppendLine($"    \"toolName\": \"{EscapeJson(snapshot.ExecutionContext.ToolName)}\",");
            sb.AppendLine($"    \"turnIndex\": \"{snapshot.ExecutionContext.TurnIndex}\",");
            sb.AppendLine($"    \"requestId\": \"{EscapeJson(snapshot.ExecutionContext.RequestId)}\",");
            sb.AppendLine($"    \"processId\": \"{Environment.ProcessId}\"");
            sb.AppendLine("  }");
            sb.AppendLine("}");

            SafeFileIO.WriteAllText(jsonPath, sb.ToString());
        }
        catch (Exception jsonEx) { Diag.WriteError("[CrashDump] 写入 JSON 快照失败", jsonEx); }

        // 2. 人类可读文本快照
        try
        {
            var txtPath = System.IO.Path.Combine(dumpDir, $"crash_{timestamp}_{snapshot.Id:N8}.log");
            var txt = new StringBuilder();
            txt.AppendLine("═══ 崩溃快照 ═══");
            txt.AppendLine($"ID:      {snapshot.Id}");
            txt.AppendLine($"时间:    {snapshot.CapturedAt:yyyy-MM-dd HH:mm:ss.fff}");
            txt.AppendLine($"来源:    {source}");
            txt.AppendLine($"围栏:    {snapshot.FenceName}");
            txt.AppendLine($"严重度:  {snapshot.Severity.ToValue()}");
            txt.AppendLine($"异常:    {snapshot.ExceptionType}: {snapshot.ExceptionMessage}");
            if (snapshot.ErrorCode is not null)
                txt.AppendLine($"错误码:  {snapshot.ErrorCode}");
            txt.AppendLine();
            txt.AppendLine("堆栈:");
            txt.AppendLine(snapshot.StackTrace ?? "(无堆栈)");
            txt.AppendLine();

            if (snapshot.ExceptionChain.Depth > 1)
            {
                txt.AppendLine($"异常链 (深度 {snapshot.ExceptionChain.Depth}):");
                foreach (var frame in snapshot.ExceptionChain.Frames)
                    txt.AppendLine($"  [{frame.Depth}] {frame.ExceptionType}: {frame.Message}");
                txt.AppendLine();
            }

            SafeFileIO.WriteAllText(txtPath, txt.ToString());
        }
        catch (Exception txtEx) { Diag.WriteError("[CrashDump] 写入文本快照失败", txtEx); }

        // 3. stderr 输出（仅 UnhandledException，避免 pipe 阻塞）
        if (source == "UnhandledException")
        {
            try
            {
                Console.Error.WriteLine($"[CRASH] {snapshot.ExceptionType}: {snapshot.ExceptionMessage}");
                Console.Error.WriteLine($"[CRASH] 快照已保存到 {dumpDir}");
            }
            catch (Exception stderrEx) { Diag.WriteError("[CrashDump] stderr 输出失败", stderrEx); }
        }
    }

    private static string EscapeJson(string? value)
    {
        if (value is null) return "";
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
    }
}
