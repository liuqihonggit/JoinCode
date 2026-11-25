namespace JoinCode;

/// <summary>
/// 程序入口点 — 显式声明应用启动流程
/// </summary>
class Program {
    static async Task<int> Main(string[] args) {
        var swTotal = PerfTimer.Start("Program.Main 总耗时");
        args = RestoreMsysPathExpansion(args);

        // 真机验收入口 — 直接复用 TUI 宿主的非交互探针，不经过参数解析/引擎双组装
        if (args is ["--selftest-tui"])
            return await NativeTui.Host.Program.RunSelftestAsync().ConfigureAwait(false);
        if (args is ["--selftest-tui-turn"])
            return await NativeTui.Host.Program.RunSelftestTurnAsync().ConfigureAwait(false);
        if (args is ["--selftest-tui-commands"])
            return await NativeTui.Host.Program.RunSelftestCommandsAsync().ConfigureAwait(false);
        if (args is ["--selftest-input"])
            return await NativeTui.Host.Program.RunSelftestInputAsync().ConfigureAwait(false);
        if (args is ["--probe-terminal-input"])
            return await NativeTui.Host.Program.RunTerminalInputProbeAsync().ConfigureAwait(false);
        if (args is ["--selftest-crossproc"])
            return await CrossProcessSelftest.RunParentAsync().ConfigureAwait(false);
        if (args.Length == 6 && args[0] == "--selftest-crossproc" && args[1] == "worker")
            return await CrossProcessSelftest.RunWorkerAsync(args[2], args[3], args[4], args[5]).ConfigureAwait(false);

        var sw0 = PerfTimer.Start("NativeDllBootstrapper");
        // 原生 DLL 引导 — 从嵌入资源释放到临时目录并注册搜索路径（必须在任何 P/Invoke 之前）
        await Entry.Startup.NativeDllBootstrapper.InitializeAsync().ConfigureAwait(false);
        sw0?.Stop();

        var sw1 = PerfTimer.Start("CaptureStartupWindow");
        // 启动时捕获前台窗口 — 此时前台窗口最可能是 jcc 自己的终端窗口
        Hands.Desktop.Win32WindowShakeService.CaptureStartupWindow();
        sw1?.Stop();

        // 密钥红线检查 — 禁止在命令行参数中传递 API Key
        var secretWarning = Cli.Output.ApiKeyRedLine.CheckArgsForSecrets(args);
        if (secretWarning is not null) {
            Cli.TerminalHelper.Init();
            App.ErrorConsole.Warning(secretWarning);
            return (int)ExitCode.ArgumentParseError;
        }

        InstallGlobalExceptionHandlers();
        using var globalJob = CreateGlobalJobObject();

        // --await N: 全局超时计时器 — 在子命令路由之前启动，确保所有路径（mcp_call/slash_call 等）都有超时保护
        // --await 值验证 — 无效值(非数字/零/负数)直接报错,避免静默降级导致无超时保护(BUG#4)
        var awaitError = ValidateAwaitArg(args);
        if (awaitError is not null) {
            Cli.TerminalHelper.Init();
            if (IsStructuredOutputFromArgs(args))
                WriteStructuredErrorToStdout(Cli.Output.CliErrorCatalog.ArgParseError(awaitError), IsNdjsonFromArgs(args));
            else
                App.ErrorConsole.Warning(awaitError);
            return (int)ExitCode.ArgumentParseError;
        }

        // --permission-mode / --format 值验证 — 无效值直接报错,避免静默接受(BUG#5/BUG#6)
        var enumError = ValidateEnumArgs(args);
        if (enumError is not null) {
            Cli.TerminalHelper.Init();
            if (IsStructuredOutputFromArgs(args))
                WriteStructuredErrorToStdout(Cli.Output.CliErrorCatalog.ArgParseError(enumError), IsNdjsonFromArgs(args));
            else
                App.ErrorConsole.Warning(enumError);
            return (int)ExitCode.ArgumentParseError;
        }

        // --quiet / -q: 静默模式 — 抑制 Warning 和 Info 输出(BUG#3)
        var isQuiet = Array.IndexOf(args, JccCliArgEnumConstants.Quiet) >= 0 || Array.IndexOf(args, "-q") >= 0;
        App.ErrorConsole.IsQuiet = isQuiet;
        if (isQuiet)
            Environment.SetEnvironmentVariable("JCC_LOG_LEVEL", "Error");
        await using var earlyAwaitTimer = StartEarlyAwaitTimer(args);

        Cli.TerminalHelper.Init();
        JoinCode.Abstractions.Shell.CommandTerminal.SetConsole(new CliCommandConsole());
        ILogger<Program>? logger = null;
        CommandLineOptions? options = null;
        try {
            // 1. 本地化 — 自动检测电脑配置语言（JCC_LANGUAGE可覆盖）
            Infrastructure.Localization.LocalizerInitializer.Initialize(
                JoinCode.Abstractions.Utils.LocalLanguageDetector.Detect());

            // 2. 帮助优先 — -h/--help 在 args 中时跳过子命令路由, 直接进入帮助系统
            var hasHelp = Array.IndexOf(args, "-h") >= 0 || Array.IndexOf(args, "--help") >= 0;

            // 3. 子命令路由 — 扫描第一个子命令（允许全局选项在前，如 jcc --trust mcp_search read）
            //    --help 时标记了 [SubCommandInfo(SelfHelp = true)] 的子命令自行处理（如 gh pr view --help 显示动态工具帮助），其余走全局帮助
            var subCmdIndex = FindSubCommandIndex(args);
            if (subCmdIndex is int idx && (!hasHelp || CliSubCommandHelpText.SelfHelpCommands.Contains(args[idx])))
                return await App.Builder.ApplicationBuilder.RunSubCommandAsync(ReorderSubCommandToFront(args, idx)).ConfigureAwait(false);

            // 4. 参数解析 → CommandLineOptions（后续全部使用 options，不再传递原始 args）
            // TASK100: --dump-nodes 是纯诊断命令，先于参数解析处理 —— --json / --verify 是它私有的
            // 修饰符，不进入全局选项表（否则会被判为未知选项而无法用于 CI 门禁）。
            if (Array.IndexOf(args, "--dump-nodes") >= 0) return DumpNodesInfo(
                json: Array.IndexOf(args, "--json") >= 0,
                verify: Array.IndexOf(args, "--verify") >= 0);

            var swParse = PerfTimer.Start("ParseArgs");
            options = App.Builder.ApplicationBuilder.ParseArgs(args);
            swParse?.Stop();
            if (options.ShowHelp) { App.Builder.ApplicationBuilder.ShowHelp(GetHelpTopic(args)); return 0; }
            if (options.ShowVersion) { App.Builder.ApplicationBuilder.ShowVersion(); return 0; }

            // --dump-profile: 诊断命令 — 输出后直接退出
            if (options.DumpProfile is not null) return DumpProfileInfo(options.DumpProfile);


            // 早期超时计时器已完成使命（子命令路由已过），主路径计时器接管 — 避免双重超时输出
            earlyAwaitTimer?.Dispose();
            await using var awaitTimer = StartAwaitTimer(options, options.IsStructuredOutputMode, logger);

            var fs = IO.FileSystem.FileSystemFactory.Create();

            // ADR 0155: AgentHubServer — 多 Agent 中央服务器，随首个 jcc 自动拉起独立进程
            // 检测端口 3457 是否有服务在运行，没有则 Process.Start 拉起 jcchub.exe
            await EnsureHubServerAsync().ConfigureAwait(false);

            // ADR 0155: 注册当前 jcc 实例到 Hub 服务端，启动心跳+消息接收
            await using var hubAgent = await RegisterHubAgentAsync().ConfigureAwait(false);

            // ADR 0149: 默认(非非交互)→ TUI, 非交互(-p)→ NonInteractiveModeRunner
            // TUI 分支: NativeTui.Host 自己组装引擎(EngineSession.CreateAsync),跳过 CreateCliSessionAsync 避免重复组装
            if (!options.IsNonInteractiveMode) {
                return await NativeTui.Host.Program.RunInteractiveAsync().ConfigureAwait(false);
            }

            var swSession = PerfTimer.Start("CreateCliSessionAsync");
            var engineResult = await App.Builder.CliSessionFactory.CreateCliSessionAsync(options, fs).ConfigureAwait(false);
            swSession?.Stop();

            var config = engineResult.Config;
            _hubAgentId = config.Provider.AgentId;
            await using var host = engineResult.Host;

            logger = host.Services.GetService<ILogger<Program>>();

            int exitCode;
            var swRun = PerfTimer.Start("NonInteractiveModeRunner.RunAsync");
            exitCode = await Entry.NonInteractiveModeRunner.RunAsync(config, options, host).ConfigureAwait(false);
            swRun?.Stop();

            swTotal?.Stop();
            return exitCode;
        } catch (OperationCanceledException) {
            // P2-7: 用户取消（Ctrl+C）或网络请求取消 — 静默退出，不写 error.log（非程序 bug）
            // 退出码 130 = POSIX 标准（128 + SIGINT=2），便于 shell 脚本区分中断与正常错误
            return (int)ExitCode.Interrupted;
        } catch (TimeoutException ex) {
            // 超时兜底 — NonInteractiveExecuteStep 已先捕获，此处处理管道其他步骤的超时
            Cli.TerminalHelper.Init();
            if (options?.IsStructuredOutputMode == true) {
                WriteStructuredErrorToStdout(Cli.Output.CliErrorCatalog.NetTimeout(ex.Message), options.IsNdjsonMode);
            } else {
                App.ErrorConsole.Warning($"请求超时: {ex.Message}");
            }
            return (int)ExitCode.LlmCallTimeout;
        } catch (ConfigurationException ex) {
            // P2-7: 配置问题 — 友好提示，不写入 error.log（非程序 bug，用户可自行修复）
            // 退出码 2 = 配置错误专用，便于 CI/脚本区分配置问题与运行时错误
            Cli.TerminalHelper.Init();
            if (options?.IsStructuredOutputMode == true) {
                var error = Cli.Output.CliErrorCatalog.ConfigInvalidValue(
                    ex.ConfigurationKey ?? "unknown",
                    ex.Message,
                    "请检查配置文件或环境变量后重试");
                WriteStructuredErrorToStdout(error, options.IsNdjsonMode);
            } else {
            App.ErrorConsole.Warning(ex.Message);
            if (!string.IsNullOrEmpty(ex.ConfigurationKey))
                    Cli.TerminalHelper.WriteError($"  配置项: {ex.ConfigurationKey}");
                if (!string.IsNullOrEmpty(ex.ConfigurationFilePath))
                    Cli.TerminalHelper.WriteError($"  配置文件: {ex.ConfigurationFilePath}");
                Cli.TerminalHelper.WriteError("  请检查配置文件或环境变量后重试。");
            }
            return (int)ExitCode.ConfigurationError;
        } catch (UriFormatException ex) {
            // 无效 endpoint URI 格式 — 配置错误，友好提示
            Cli.TerminalHelper.Init();
            if (options?.IsStructuredOutputMode == true) {
                var error = Cli.Output.CliErrorCatalog.ConfigInvalidValue(
                    "endpoint",
                    ex.Message,
                    "请检查 JCC_ENDPOINT 环境变量或配置文件中的端点格式（如 http://localhost:8091/v1）");
                WriteStructuredErrorToStdout(error, options.IsNdjsonMode);
            } else {
                App.ErrorConsole.Warning($"端点格式无效: {ex.Message}");
                Cli.TerminalHelper.WriteError("  请检查 JCC_ENDPOINT 环境变量或配置文件中的端点格式。");
            }
            return (int)ExitCode.ConfigurationError;
        } catch (NotSupportedException ex) when (ex.Message.Contains("scheme", StringComparison.OrdinalIgnoreCase)) {
            // 不支持的 URI scheme（如 ftp://）— 配置错误，友好提示
            Cli.TerminalHelper.Init();
            if (options?.IsStructuredOutputMode == true) {
                var error = Cli.Output.CliErrorCatalog.ConfigInvalidValue(
                    "endpoint",
                    ex.Message,
                    "请检查 JCC_ENDPOINT 环境变量或配置文件中的端点格式（仅支持 http/https）");
                WriteStructuredErrorToStdout(error, options.IsNdjsonMode);
            } else {
                App.ErrorConsole.Warning($"端点协议不支持: {ex.Message}");
                Cli.TerminalHelper.WriteError("  请检查 JCC_ENDPOINT 环境变量或配置文件中的端点格式（仅支持 http/https）。");
            }
            return (int)ExitCode.ConfigurationError;
        } catch (Exception ex) when (ex is OutOfMemoryException or TypeInitializationException) {
            // P2-7: 不可恢复异常 — 记录日志后 rethrow 让进程崩溃（继续运行可能损坏数据）
            await WriteErrorLogAsync(ex, fatal: true, logger).ConfigureAwait(false);
            throw;
        } catch (Exception ex) {
            // 通用异常 — 记录日志并友好提示
            // 视角2 #27: 使用 ErrorConsole.Fatal 渲染（红色致命错误 + 图标）
            // 诊断: 完整异常信息写入 error.log 供调试，不直接打印堆栈给用户
            Diag.WriteLine($"[MAIN] 未捕获异常: {ex.GetType().FullName}: {ex.Message}");
            Diag.WriteLine($"[MAIN] StackTrace:\n{ex.StackTrace}");
            if (ex.InnerException is not null)
                Diag.WriteLine($"[MAIN] InnerException: {ex.InnerException.GetType().FullName}: {ex.InnerException.Message}\n{ex.InnerException.StackTrace}");

            var errorLog = await WriteErrorLogAsync(ex, logger: logger).ConfigureAwait(false);

            Cli.TerminalHelper.Init();
            if (options?.IsStructuredOutputMode == true) {
                WriteStructuredErrorToStdout(new Cli.Output.CliStructuredError(
                    "RUNTIME_ERROR", ex.Message,
                    hint: $"详细日志: {Cli.Output.XdgPathResolver.ShortenPath(errorLog)}",
                    retryable: false), options.IsNdjsonMode);
            } else {
                App.ErrorConsole.Fatal(ex.Message);
                Cli.TerminalHelper.WriteError($"  详细日志: {Cli.Output.XdgPathResolver.ShortenPath(errorLog)}");
            }

            return (int)ExitCode.GeneralError;
        } finally {
            // ADR 0155: Agent 注销 — 进程退出时通知 Hub 服务端
            if (!string.IsNullOrEmpty(_hubAgentId)) {
                try {
                    await EngineApplicationBuilder.UnregisterAgentAsync("http://localhost:3457", _hubAgentId).ConfigureAwait(false);
                } catch (Exception ex) {
                    Diag.WriteLine($"[HUB] Agent 注销失败: {ex.Message}");
                }
            }
        }
    }

    private static string? _hubAgentId;

    /// <summary>
    /// --dump-nodes: 输出所有 Profile 中定义的节点列表（诊断用）
    /// </summary>
    private static int DumpNodesInfo(bool json = false, bool verify = false) {
        var profiles = Composition.NodeRegistry.NodeProfileLoader.LoadFromResources(fileSystem: new IO.FileSystem.PhysicalFileSystem());
        var resolver = new Composition.NodeRegistry.NodeProfileResolver(profiles);

        // G5: 真机展示 NodeRegistry 从编译期工厂 catalog 自动归纳的节点数 ——
        // 与 profile 配置节点交叉核对；空容器只用于读取归纳结果，不解析任何服务。
        var emptySp = new Microsoft.Extensions.DependencyInjection.ServiceCollection().BuildServiceProvider();
        // 工厂清单 = 装配层宽闭包（Composition）+ 本程序集自己的目录。
        // 为什么两段：生成器对 Exe 只扫自身程序集，而 cli-commands 节点的实现就在本程序集
        // （命令执行器是 internal static，无法搬到下游库）；装配层的闭包看不到它们，
        // 不合并就会把 18 条声明全部报成「声明无工厂」。
        // TASK100 S8（§24.2）：合并走 NodeServiceResolver.BuildWithFallback —— 与运行期装配
        // 同一个函数（手写优先、catalog 兜底），否则「诊断看到的」与「运行时装配的」又成两份真相。
        // 两份来源分别留一份，门禁才能核对「实例型节点由手写负责、声明型节点由 catalog 负责」。
        var manualFactories = Pipelines.DataDrivenPipelineComposition.BuildLocalNodeFactories()
            .GroupBy(static kv => kv.Key, StringComparer.Ordinal)
            .ToDictionary(static g => g.Key, static g => g.First().Value, StringComparer.Ordinal);
        var catalogFactories = Core.DependencyInjection.NodeFactoryContribution.Create()
            .Concat(Core.DependencyInjection.NodeFactoryCatalog_Jcc.Create())
            .GroupBy(static kv => kv.Key, StringComparer.Ordinal)
            .ToDictionary(static g => g.Key, static g => g.First().Value, StringComparer.Ordinal);
        var factories = Composition.NodeRegistry.NodeServiceResolver
            .BuildWithFallback(manualFactories, catalogFactories)
            .Factories
            .ToArray();
        var factorySources = manualFactories.Keys
            .Union(catalogFactories.Keys, StringComparer.Ordinal)
            .ToDictionary(
                key => key,
                key => (manualFactories.ContainsKey(key), catalogFactories.ContainsKey(key)) switch {
                    (true, true) => "both",
                    (true, false) => "handwritten",
                    _ => "catalog"
                },
                StringComparer.Ordinal);
        var registry = Composition.NodeRegistry.NodeRegistry.BuildFromCatalog(emptySp, factories);

        if (!json) {
            Cli.TerminalHelper.WriteLine("═══ 已注册节点列表 ═══");
            Cli.TerminalHelper.WriteLine();
            Cli.TerminalHelper.WriteLine($"NodeRegistry 自动归纳: {registry.NodeCount} 个节点（来自编译期工厂 catalog，无需手工登记）");
            Cli.TerminalHelper.WriteLine();
        }

        // G1 + TASK098 自检只在文本模式的 debug 日志下跑，避免污染 --json 输出。
        if (!json && Environment.GetEnvironmentVariable("JCC_DEBUGLOG") == "1") {
            var probe = Composition.NodeRegistry.NodeRegistry.BuildFromCatalog(emptySp, []);
            var h1 = probe.Register("probe", (_, _) => new ProbeNode("first"));
            var h2 = probe.Register("probe", (_, _) => new ProbeNode("second"));
            h2.Replace((_, _) => new ProbeNode("replaced"));
            var before = probe.TryGetNode("probe", out var bn) ? bn?.NodeId ?? "(null)" : "(missing)";
            h2.Dispose();
            var after = probe.TryGetNode("probe", out var an) ? an?.NodeId ?? "(null)" : "(missing)";
            h1.Dispose();
            var end = probe.TryGetNode("probe", out _) ? "still-present" : "removed";
            Cli.TerminalHelper.WriteLine($"[G1-SELFTEST] after-replace={before} after-lifo-dispose={after} after-all-disposed={end}");
            Cli.TerminalHelper.WriteLine();

            // TASK098 融合自检 —— 一个 CompositionHandle 同时兑现 G1 逆序回滚 + G6 H₀ 恢复。
            // 走生产 DeltaComposer.Compose（Permanent/Round 统一入口）+ Track（G1 挂接）+ Dispose（双回滚）。
            var skeleton = resolver.Resolve("base");
            var round = Composition.Improvement.CompositionDelta.FromExperience(
                new Composition.Improvement.HarnessPatch("chat-pipeline", "stream-rate-limit-mw", "max-requests", "50"),
                "selftest", "真机自检");
            var handle = Composition.Improvement.DeltaComposer.Compose(skeleton, [round]);
            FusionMax(handle.Current, out var currentMax);
            var regA = probe.Register("fusion", (_, _) => new ProbeNode("reg-A"));
            var regB = probe.Register("fusion", (_, _) => new ProbeNode("reg-B"));
            handle.Track(regA);
            handle.Track(regB);
            var regBefore = probe.TryGetNode("fusion", out var fb) ? fb?.NodeId ?? "(null)" : "(missing)";
            handle.Dispose();
            FusionMax(handle.Current, out var restoredMax);
            var regAfter = probe.TryGetNode("fusion", out _) ? "still-present" : "removed";
            Cli.TerminalHelper.WriteLine($"[T098-FUSION] current-max={currentMax} after-dispose-max={restoredMax} reg-before={regBefore} reg-after={regAfter} disposed={handle.IsDisposed}");
            Cli.TerminalHelper.WriteLine();
        }

        // ── TASK100：profile 声明 ↔ 工厂 catalog 的三方比对 ──
        var declaredKeys = new HashSet<string>(StringComparer.Ordinal);
        var exclusiveDuplicates = new List<string>();
        var consumerReports = new List<NodeDumpConsumerDto>();
        var contractCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var profileName in profiles.Keys.Order(StringComparer.Ordinal)) {
            var resolved = resolver.Resolve(profileName);
            if (!json) Cli.TerminalHelper.WriteLine($"Profile: {profileName}");

            foreach (var (consumerId, consumerConfig) in resolved.Consumers.OrderBy(kv => kv.Key, StringComparer.Ordinal)) {
                foreach (var node in consumerConfig.Nodes) declaredKeys.Add($"{consumerId}:{node.Id}");

                // 互斥契约（provider/role）内同一 ID 出现两次必然是配置错误。
                if (consumerConfig.Contract is Abstractions.Composition.Profile.NodeContract.Provider or Abstractions.Composition.Profile.NodeContract.Role) {
                    foreach (var duplicate in consumerConfig.Nodes.GroupBy(n => n.Id, StringComparer.Ordinal).Where(g => g.Count() > 1))
                        exclusiveDuplicates.Add($"{profileName}:{consumerId}:{duplicate.Key}");
                }

                contractCounts[consumerConfig.Contract] = contractCounts.GetValueOrDefault(consumerConfig.Contract) + 1;

                var ordered = consumerConfig.Nodes.OrderBy(n => n.Priority).ToArray();
                consumerReports.Add(new NodeDumpConsumerDto(
                    profileName, consumerId, consumerConfig.Contract,
                    ordered.Select(n => new NodeDumpEntryDto(n.Id, n.Priority, n.EnabledWhenFeatureFlag, n.Config)).ToArray()));

                if (json) continue;

                // 契约直接写在消费点行首 —— 一眼能看出这个消费点是叠加、互斥还是存在即提供。
                Cli.TerminalHelper.WriteLine($"  消费点: {consumerId}  [contract={consumerConfig.Contract}, {ordered.Length} 节点]");
                foreach (var node in ordered) {
                    var flag = node.EnabledWhenFeatureFlag is not null ? $" [flag:{node.EnabledWhenFeatureFlag}]" : "";
                    // G4: metadata 从 profile config 的 meta.* 保留键注入 —— 真机可见配置真实值，
                    // 未声明的节点不占位（保持诊断输出简洁）。
                    var meta = Abstractions.Composition.NodeMetadata.FromConfig(node.Config);
                    var metaText = meta is null
                        ? ""
                        // 不输出 priority —— 路由以行首 (priority=N) 为准，meta.priority 不存在。
                        : $" [meta v={meta.Version} deps={FormatMetaList(meta.Dependencies)} caps={FormatMetaList(meta.Capabilities)} req={FormatMetaList(meta.RequiredCapabilities)}]";
                    Cli.TerminalHelper.WriteLine($"    - {node.Id} (priority={node.Priority}){flag}{metaText}{FormatGovernedTools(consumerConfig.Contract, node.Id)}");
                }
            }
            if (!json) Cli.TerminalHelper.WriteLine();
        }

        var factoryKeys = factories.Select(f => f.Key).ToHashSet(StringComparer.Ordinal);
        var verification = new NodeDumpVerificationDto(
            DeclaredWithoutFactory: declaredKeys.Except(factoryKeys, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            FactoryWithoutDeclaration: factoryKeys.Except(declaredKeys, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            DuplicateInExclusiveConsumer: exclusiveDuplicates.Order(StringComparer.Ordinal).ToArray());

        if (json) {
            var report = new NodeDumpReportDto(
                registry.NodeCount,
                consumerReports.Count,
                consumerReports.ToArray(),
                verification,
                factorySources);
            Cli.TerminalHelper.WriteLine(System.Text.Json.JsonSerializer.Serialize(report, NodeDumpJsonContext.Default.NodeDumpReportDto));
            return verify && !verification.IsConsistent ? 1 : 0;
        }

        Cli.TerminalHelper.WriteLine($"契约分布: {string.Join(", ", contractCounts.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key}={kv.Value}"))}");
        Cli.TerminalHelper.WriteLine();

        if (verify) {
            Cli.TerminalHelper.WriteLine("═══ 节点一致性校验 ═══");
            Cli.TerminalHelper.WriteLine($"  声明无工厂: {FormatViolations(verification.DeclaredWithoutFactory)}");
            Cli.TerminalHelper.WriteLine($"  工厂无声明: {FormatViolations(verification.FactoryWithoutDeclaration)}");
            Cli.TerminalHelper.WriteLine($"  互斥契约内重复: {FormatViolations(verification.DuplicateInExclusiveConsumer)}");
            Cli.TerminalHelper.WriteLine(verification.IsConsistent
                ? "  结果: [OK] 声明 ↔ 工厂 ↔ 契约 三方一致"
                : "  结果: [FAIL] 存在不一致 —— 死声明要接上或删除，孤儿工厂要登记到 profile");
            return verification.IsConsistent ? 0 : 1;
        }

        return 0;
    }

    /// <summary>一致性违规列表格式化 — 空列表输出 [OK]。</summary>
    private static string FormatViolations(string[] items) =>
        items.Length == 0 ? "[OK] none" : $"[FAIL] {string.Join(", ", items)}";

    /// <summary>TASK098 融合自检辅助：从 profile 读限流阈值，读不到给占位符。</summary>
    private static void FusionMax(Abstractions.Composition.Profile.ResolvedProfile profile, out string value) {
        value = profile.Consumers.TryGetValue("chat-pipeline", out var c)
            ? c.Nodes.FirstOrDefault(n => n.Id == "stream-rate-limit-mw")?.Config.GetValueOrDefault("max-requests") ?? "(null)"
            : "(null)";
    }

    /// <summary>
    /// 能力节点的「治理面」标注 —— 这个能力关掉会连带裁掉多少工具。
    /// <para>
    /// 只对 capability 契约生效，且数量为 0 时不输出（<c>dream</c> 不治理工具面，
    /// 打个 <c>tools=0</c> 反而会让人以为节点坏掉了）。
    /// </para>
    /// </summary>
    /// <param name="contract">消费点契约。</param>
    /// <param name="nodeId">节点 ID。</param>
    /// <returns>形如 <c> [tools=4]</c> 的文本；不适用时为空串。</returns>
    private static string FormatGovernedTools(string contract, string nodeId) {
        if (contract is not Abstractions.Composition.Profile.NodeContract.Capability)
            return "";
        var governed = Composition.NodeRegistry.CapabilityToolCategories.GovernedToolCount(nodeId);
        return governed > 0 ? $" [tools={governed}]" : "";
    }

    private static string FormatMetaList(string[] items) =>
        items.Length == 0 ? "-" : string.Join(",", items);

    /// <summary>G1 真机自检用的最小 INode 实现（只在 --dump-nodes debug 自检里构造）。</summary>
    private sealed class ProbeNode(string id) : Abstractions.Composition.INode {
        /// <summary>自检节点 ID。</summary>
        public string NodeId { get; } = id;

        /// <summary>自检节点元数据（用唯一默认值）。</summary>
        public Abstractions.Composition.NodeMetadata Metadata { get; } = Abstractions.Composition.NodeMetadata.Unspecified;

        /// <summary>自检节点状态。</summary>
        public Abstractions.Composition.NodeState State { get; } = Abstractions.Composition.NodeState.Active;
    }

    /// <summary>
    /// --dump-profile: 输出指定 Profile 的解析结果（诊断用）
    /// </summary>
    private static int DumpProfileInfo(string profileName) {
        var profiles = Composition.NodeRegistry.NodeProfileLoader.LoadFromResources(fileSystem: new IO.FileSystem.PhysicalFileSystem());
        var resolver = new Composition.NodeRegistry.NodeProfileResolver(profiles);

        if (!profiles.ContainsKey(profileName)) {
            App.ErrorConsole.Warning($"Profile '{profileName}' 不存在。可用 Profile: {string.Join(", ", profiles.Keys.Order())}");
            return (int)ExitCode.ArgumentParseError;
        }

        var resolved = resolver.Resolve(profileName);

        Cli.TerminalHelper.WriteLine($"═══ Profile: {profileName} ═══");
        Cli.TerminalHelper.WriteLine();

        foreach (var (consumerId, consumerConfig) in resolved.Consumers.OrderBy(kv => kv.Key)) {
            Cli.TerminalHelper.WriteLine($"消费点: {consumerId}");
            foreach (var node in consumerConfig.Nodes.OrderBy(n => n.Priority)) {
                var flag = node.EnabledWhenFeatureFlag is not null ? $" [flag:{node.EnabledWhenFeatureFlag}]" : "";
                var config = node.Config.Count > 0 ? $" config={{{string.Join(",", node.Config.Select(kv => $"{kv.Key}={kv.Value}"))}}}" : "";
                Cli.TerminalHelper.WriteLine($"  - {node.Id} (priority={node.Priority}){flag}{config}");
            }
            Cli.TerminalHelper.WriteLine();
        }

        return 0;
    }

    /// <summary>
    /// 写入错误日志到临时目录的 jcc_error.log。
    /// <para><b>默认不写堆栈</b>：这个文件路径会回显给用户（「详细日志: ~\.jcc\runtime\jcc_error.log」），
    /// 写进去的每一行用户都会当成「程序崩了」的证据。堆栈是给维护者看的，只在
    /// <c>JCC_DEBUGLOG=1</c> 时落盘；默认形态是「异常类型 + 消息 + 一行来源」，
    /// 足够定位问题出在哪一层，也不至于让用户读到 <c>at JoinCode.App.Builder...</c>。
    /// 完整堆栈另有归处：崩溃快照目录（crash-dumps）。</para>
    /// </summary>
    /// <param name="ex">异常对象</param>
    /// <param name="fatal">是否为致命异常（标记 [FATAL] 前缀）</param>
    /// <returns>错误日志文件路径</returns>
    private static async Task<string> WriteErrorLogAsync(Exception ex, bool fatal = false, ILogger? logger = null) {
        var errorLog = Cli.Output.XdgPathResolver.GetErrorLogPath();
        var prefix = fatal ? "[FATAL] " : string.Empty;
        var errorContent = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {prefix}{ex.GetType().FullName}: {ex.Message}";

        if (ex.InnerException is not null)
            errorContent += $"\n  inner: {ex.InnerException.GetType().FullName}: {ex.InnerException.Message}";

        // 一行来源（首个带文件信息的栈帧）— 让维护者不打开调试也能看出发生在哪个方法
        var origin = FirstStackOrigin(ex);
        if (origin is not null)
            errorContent += $"\n  origin: {origin}";

        if (Diag.IsDebugLog) {
            errorContent += $"\n{ex.StackTrace}";
            if (ex.InnerException is not null)
                errorContent += $"\n--- InnerException ---\n{ex.InnerException.GetType().FullName}: {ex.InnerException.Message}\n{ex.InnerException.StackTrace}";
        } else {
            errorContent += "\n  （需要完整堆栈：设置 JCC_DEBUGLOG=1 后复现，或查看 ~/.jcc/runtime/crash-dumps）";
        }

        try {
            var dir = System.IO.Path.GetDirectoryName(errorLog);
            if (dir is not null && !System.IO.Directory.Exists(dir))
                System.IO.Directory.CreateDirectory(dir);
            await SafeFileIO.WriteAllText(errorLog, errorContent).ConfigureAwait(false);
        } catch (Exception logEx) {
            Diag.WriteLine($"[MAIN] WriteErrorLog 失败: {logEx.GetType().Name}: {logEx.Message}");
            logger?.LogWarning(logEx, "写入错误日志失败");
        }
        return errorLog;
    }

    /// <summary>
    /// 取首个带源文件信息的栈帧（<c>in /path/File.cs:line N</c>），压成一行的 <c>类型.方法 (文件:行)</c>。
    /// </summary>
    private static string? FirstStackOrigin(Exception ex) {
        var stack = ex.StackTrace;
        if (string.IsNullOrEmpty(stack)) return null;

        foreach (var rawLine in stack.Split('\n')) {
            var line = rawLine.Trim();
            if (!line.StartsWith("at ", StringComparison.Ordinal)) continue;

            var inIndex = line.IndexOf(" in ", StringComparison.Ordinal);
            if (inIndex < 0) continue;

            var method = line[3..inIndex].Trim();
            var location = line[(inIndex + 4)..].Trim();
            return $"{method} ({location})";
        }

        // 没有源文件信息时退而求其次：取最深一帧的方法名
        foreach (var rawLine in stack.Split('\n')) {
            var line = rawLine.Trim();
            if (line.StartsWith("at ", StringComparison.Ordinal))
                return line[3..].Trim();
        }
        return null;
    }

    /// <summary>
    /// 写入结构化 JSON 错误到 stderr — 供 JSON 模式下的异常捕获链使用
    /// </summary>
    private static void WriteJsonError(Cli.Output.CliStructuredError error) {
        var contract = new Cli.Output.CliOutputContract(
            jsonMode: true,
            jsonContext: Cli.Output.CliOutputJsonContext.Default);
        contract.WriteError(error);
    }

    /// <summary>
    /// 写入结构化错误到 stdout — Bug 14: 支持 JSON 信封和 NDJSON 事件两种格式
    /// <para>JSON 模式: 输出 {ok:false, error:{...}} 信封</para>
    /// <para>NDJSON 模式: 输出 {type:"error", data:{error:{...}}} 事件</para>
    /// </summary>
    private static void WriteStructuredErrorToStdout(Cli.Output.CliStructuredError error, bool isNdjson) {
        try {
            if (isNdjson) {
                var resultMessage = Cli.Output.SdkResultMessage.Create(System.Guid.NewGuid().ToString("N")) with {
                    Subtype = "error_during_execution",
                    IsError = true,
                    Result = null,
                    ModelUsage = new(),
                    Errors = [error.Message],
                };
                var json = RelaxedJsonSerializer.Serialize(resultMessage, Cli.Output.CliOutputJsonContext.Default);
                Console.Out.WriteLine(json);
            } else {
                var envelope = Cli.Output.CliOutputEnvelope.Fail(error);
                Console.Out.WriteLine(envelope.ToString());
            }
            Console.Out.Flush();
        } catch (Exception ex) {
            Diag.WriteLine($"[MAIN] 写入结构化错误失败: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Bug 10 修复：--await 超时时结构化输出模式输出错误信封到 stdout。
    /// <para>写 stdout 而非 stderr — stderr 重定向到未读取 pipe 时会阻塞导致 Environment.Exit 永不执行（见 StartAwaitTimer remarks）。
    /// stdout 在结构化输出模式下由消费者读取，不会阻塞。消费者通过 ok:false 或 type:"error" 判断为错误。</para>
    /// <para>Bug 14: 支持 NDJSON 事件格式（isNdjson=true 时输出 {type:"error",data:{error:{...}}}）</para>
    /// </summary>
    /// <param name="seconds">超时秒数</param>
    /// <param name="isNdjson">是否 NDJSON 模式（false=JSON 信封模式）</param>
    private static void WriteAwaitTimeoutJsonToStdout(int seconds, bool isNdjson = false) {
        try {
            var error = Cli.Output.CliErrorCatalog.TimeoutAwaitDiagnostic(seconds);
            WriteStructuredErrorToStdout(error, isNdjson);
        } catch (Exception ex) {
            Diag.WriteLine($"[MAIN] 写入超时 JSON 错误信封失败: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>原始参数里的输出形状（options 解析前的最小判定）</summary>
    private enum RawOutputMode {
        /// <summary>人类可读文本</summary>
        Text,
        /// <summary>单个 JSON 信封</summary>
        Json,
        /// <summary>逐行 JSON 事件流</summary>
        Ndjson,
    }

    /// <summary>
    /// 扫描原始 args 判定输出形状 — 供 <see cref="StartEarlyAwaitTimer"/> 等在
    /// <c>CommandLineOptions</c> 解析前就要决定「错误信封长什么样」的路径使用。
    /// <para>TASK138：SDK 方言 <c>--output-format stream-json</c> 等价 <c>--format ndjson</c>；
    /// <c>--input-format stream-json</c> 隐含输出为 ndjson（应用层归一），所以也算进来 ——
    /// 否则超时/参数错误会把人类可读文本写进协议流，SDK 客户端 JSON 解析直接崩。</para>
    /// </summary>
    /// <param name="args">原始命令行参数</param>
    /// <returns>输出形状</returns>
    private static RawOutputMode ScanRawOutputMode(string[] args) {
        var mode = RawOutputMode.Text;
        for (var i = 0; i < args.Length; i++) {
            if (args[i] == JccCliArgEnumConstants.Json)
                mode = RawOutputMode.Json; // 不提前返回：后出现的显式流式格式应胜出（与改造前「两个探测各自独立扫描」一致）

            var isOutputSide = args[i] == JccCliArgEnumConstants.Format || args[i] == JccCliArgEnumConstants.OutputFormat;
            var isInputSide = args[i] == JccCliArgEnumConstants.InputFormat;
            if ((!isOutputSide && !isInputSide) || i + 1 >= args.Length)
                continue;

            var value = args[i + 1];
            if (string.Equals(value, OutputFormatEnumConstants.Json, StringComparison.OrdinalIgnoreCase))
                mode = RawOutputMode.Json;
            else if (string.Equals(value, OutputFormatEnumConstants.Ndjson, StringComparison.OrdinalIgnoreCase)
                     || string.Equals(value, "stream-json", StringComparison.OrdinalIgnoreCase))
                return RawOutputMode.Ndjson; // 显式流式优先于先前看到的 --format json
        }
        return mode;
    }

    /// <summary>
    /// 从原始 args 中检测是否启用结构化输出模式（JSON 或 NDJSON）— 供 StartEarlyAwaitTimer 在 options 解析前使用。
    /// </summary>
    private static bool IsStructuredOutputFromArgs(string[] args) => ScanRawOutputMode(args) != RawOutputMode.Text;

    /// <summary>
    /// 从原始 args 中检测是否启用 NDJSON 模式 — 供 StartEarlyAwaitTimer 在 options 解析前使用。
    /// </summary>
    private static bool IsNdjsonFromArgs(string[] args) => ScanRawOutputMode(args) == RawOutputMode.Ndjson;

    /// <summary>
    /// 启动 --await N 超时计时器。
    /// N 秒后强制退出进程并返回 ExitCode.AwaitTimeout (=1234)，用于诊断卡死问题。
    /// 正常完成时 using 释放计时器，不影响返回值。
    /// </summary>
    /// <remarks>
    /// ⚠️ Timer 回调中禁止在 <see cref="Environment.Exit(int)"/> 之前写 Console.Error：
    /// 当 stderr 被重定向到未读取的 pipe（如 PowerShell <c>Start-Process -RedirectStandardError</c>）时，
    /// Console.Error.WriteLine 会阻塞，导致 <see cref="Environment.Exit(int)"/> 永远不执行，
    /// 进程卡死。详见 <c>docs/ai_interaction_docs/MockServer测试问题清单.md</c> P2-1。
    /// 启动时的日志 + ExitCode=AwaitTimeout 已足够诊断超时触发。
    /// </remarks>
    private static System.Threading.Timer? StartAwaitTimer(CommandLineOptions options, bool isStructuredOutput, ILogger? logger = null) {
        if (options.AwaitTimeoutSeconds is not { } seconds || seconds <= 0)
            return null;

        Diag.WriteLine($"[MAIN] --await {seconds}s 计时器已启动（超时返回{(int)ExitCode.AwaitTimeout}）");

        return new System.Threading.Timer(
            callback: _ => {
                // ⚠️ 禁止在 Environment.Exit 之前写 Console.Error（详见方法 remarks 注释）
                // Bug 10 修复：结构化输出模式下写错误信封到 stdout（非 stderr，避免 pipe 阻塞）
                if (isStructuredOutput)
                    WriteAwaitTimeoutJsonToStdout(seconds, options.IsNdjsonMode);

                // 超时诊断降级：写时间戳文件留审计轨迹（文件写不依赖 Console，不会因 pipe 阻塞）
                try {
                    var timeoutLog = Cli.Output.XdgPathResolver.GetAwaitTimeoutLogPath();
                    SafeFileIO.AppendAllText(timeoutLog,
                        $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] --await {seconds}s 超时, 进程强制退出({(int)ExitCode.AwaitTimeout})\n");
                } catch (Exception logEx) {
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
    /// 恢复 MSYS2/Git Bash 路径展开 — 将被展开的斜杠命令参数恢复为原始 /命令名 格式。
    /// <para>Git Bash 会将以 / 开头的参数视为 Unix 路径并展开为 Windows 路径</para>
    /// <para>例: /compact → C:/Program Files/Git/compact → 恢复为 /compact</para>
    /// <para>还原规则本身定义在契约层 <see cref="CliArgNormalizer"/>：
    /// jcc 主程序、<c>jcc tool call</c> 执行器、各分组 exe 共用同一份实现，禁止在此重写。</para>
    /// </summary>
    private static string[] RestoreMsysPathExpansion(string[] args) {
        // 参数数量上限是 CLI 边界的拒绝服务防护，属于本层职责；
        // 逐参数还原逻辑属于契约层，委托过去保证三处消费方行为一致。
        if (args.Length == 0 || args.Length > CliGuardConfig.MaxArgCount)
            return args;
        return CliArgNormalizer.RestoreMsysPathExpansion(args);
    }

    /// <summary>
    /// 验证 --await 参数值 — 无效值(非数字/零/负数)返回错误消息,避免静默降级导致无超时保护。
    /// </summary>
    private static string? ValidateAwaitArg(string[] args) {
        for (var i = 0; i < args.Length - 1; i++) {
            if (args[i] == JccCliArgEnumConstants.Await) {
                var value = args[i + 1];
                if (!int.TryParse(value, out var seconds))
                    return $"--await 的值 '{value}' 不是有效整数，请使用正整数（如 --await 10）";
                if (seconds <= 0)
                    return $"--await 的值 {seconds} 必须为正整数，请使用大于 0 的值（如 --await 10）";
            }
        }
        return null;
    }

    /// <summary>
    /// 验证 --permission-mode 和 --format 参数值 — 无效值返回错误消息,避免静默接受。
    /// <para>BUG#5: --permission-mode 有效值 plan/auto/ask/bypass</para>
    /// <para>BUG#6: --format 有效值 text/json/ndjson</para>
    /// </summary>
    private static string? ValidateEnumArgs(string[] args) {
        var validPermissionModes = new[] { PermissionModeEnumConstants.Plan, PermissionModeEnumConstants.Auto, PermissionModeEnumConstants.Ask, PermissionModeEnumConstants.Bypass };
        // stream-json 是 SDK 方言（应用层归一为 ndjson）— 此处放行
        var validFormats = new[] { OutputFormatEnumConstants.Text, OutputFormatEnumConstants.Json, OutputFormatEnumConstants.Ndjson, "stream-json" };
        var validInputFormats = new[] { "stream-json" };

        for (var i = 0; i < args.Length - 1; i++) {
            if (args[i] == JccCliArgEnumConstants.PermissionMode) {
                var value = args[i + 1];
                if (!validPermissionModes.Contains(value, StringComparer.OrdinalIgnoreCase))
                    return $"--permission-mode 的值 '{value}' 无效，有效值为: {string.Join(", ", validPermissionModes)}";
            }
            if (args[i] == JccCliArgEnumConstants.Format || args[i] == JccCliArgEnumConstants.OutputFormat) {
                var value = args[i + 1];
                if (!validFormats.Contains(value, StringComparer.OrdinalIgnoreCase))
                    return $"{args[i]} 的值 '{value}' 无效，有效值为: {string.Join(", ", validFormats)}";
            }
            if (args[i] == JccCliArgEnumConstants.InputFormat) {
                var value = args[i + 1];
                if (!validInputFormats.Contains(value, StringComparer.OrdinalIgnoreCase))
                    return $"--input-format 的值 '{value}' 无效，有效值为: {string.Join(", ", validInputFormats)}";
            }
        }
        return null;
    }

    /// <summary>
    /// 从原始 args 中解析 --await N 并启动早期超时计时器。
    /// 在子命令路由（mcp_call/slash_call 等）之前启动，确保所有路径都有超时保护。
    /// 主路径的 StartAwaitTimer 会再次启动（using 释放时先释放 early，再释放 late，不冲突）。
    /// </summary>
    private static System.Threading.Timer? StartEarlyAwaitTimer(string[] args) {
        for (var i = 0; i < args.Length - 1; i++) {
            if (args[i] == JccCliArgEnumConstants.Await && int.TryParse(args[i + 1], out var seconds) && seconds > 0) {
                Diag.WriteLine($"[MAIN] --await {seconds}s 早期计时器已启动（超时返回{(int)ExitCode.AwaitTimeout}）");
                var earlyIsStructuredOutput = IsStructuredOutputFromArgs(args);
                var earlyIsNdjson = IsNdjsonFromArgs(args);
                return new System.Threading.Timer(
                    callback: _ => {
                        // Bug 10 修复：结构化输出模式下写错误信封到 stdout
                        if (earlyIsStructuredOutput)
                            WriteAwaitTimeoutJsonToStdout(seconds, earlyIsNdjson);

                        try {
                            var timeoutLog = Cli.Output.XdgPathResolver.GetAwaitTimeoutLogPath();
                            SafeFileIO.AppendAllText(timeoutLog,
                                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] --await {seconds}s 早期超时, 进程强制退出({(int)ExitCode.AwaitTimeout})\n");
                        } catch (Exception logEx) { Diag.WriteLine($"[MAIN] 早期超时日志写入失败: {logEx.Message}"); }
                        Environment.Exit((int)ExitCode.AwaitTimeout);
                    },
                    state: null,
                    dueTime: TimeSpan.FromSeconds(seconds),
                    period: System.Threading.Timeout.InfiniteTimeSpan);
            }
        }
        return null;
    }

    /// <summary>
    /// 安装全局异常钩子 — 捕获未处理异常和未观察的 Task 异常，写入崩溃快照和结构化日志
    /// </summary>
    private static void InstallGlobalExceptionHandlers() {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => {
            var ex = e.ExceptionObject as Exception ?? new Exception("未知异常");
            WriteCrashDump(ex, source: "UnhandledException");
        };

        TaskScheduler.UnobservedTaskException += (_, e) => {
            WriteCrashDump(e.Exception, source: "UnobservedTaskException");
            e.SetObserved();
        };
    }

    /// <summary>
    /// 创建全局 JobObject — 把当前进程加入 KILL_ON_JOB_CLOSE，所有子进程自动继承。
    /// 父进程无论正常退出还是崩溃，OS 自动 Kill 整个进程树，避免子进程孤儿化。
    /// 非 Windows 平台或创建失败时返回 null，不阻塞启动。
    /// </summary>
    private static WindowsJobObjectSandbox? CreateGlobalJobObject() {
        if (!OperatingSystem.IsWindows()) return null;
        try {
            var job = new WindowsJobObjectSandbox();
            job.CreateJobObject();
            job.AssignProcess(Environment.ProcessId);
            return job;
        } catch (Exception) {
            return null;
        }
    }

    /// <summary>
    /// 写入崩溃快照到临时目录 — 结构化 JSON + 人类可读文本
    /// 路径由 XdgPathResolver.GetCrashDumpsDirectory() 统一管理
    /// </summary>
    private static void WriteCrashDump(Exception exception, string source) {
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var dumpDir = Cli.Output.XdgPathResolver.GetCrashDumpsDirectory();
        try { System.IO.Directory.CreateDirectory(dumpDir); } catch (Exception dirEx) { Diag.WriteError("[CrashDump] 创建目录失败", dirEx); }

        var snapshot = new CrashSnapshot(
            fenceName: $"Global.{source}",
            severity: CrashSeverity.Fatal,
            exception: exception,
            executionContext: new CrashExecutionContext {
                OperationName = source
            }.With("processId", Environment.ProcessId.ToString()));

        // 1. 结构化 JSON 快照（DTO + JsonSerializer，AOT 安全）
        var idShort = snapshot.Id.ToString("N")[..8];
        try {
            var jsonPath = System.IO.Path.Combine(dumpDir, $"crash_{timestamp}_{idShort}.json");
            var dto = new CrashDumpSnapshotDto(
                snapshot.Id,
                snapshot.CapturedAt,
                snapshot.FenceName,
                snapshot.Severity.ToValue(),
                snapshot.ExceptionType,
                snapshot.ExceptionMessage,
                snapshot.ErrorCode,
                snapshot.StackTrace,
                source,
                [..snapshot.ExceptionChain.Frames.Select(f => new CrashDumpFrameDto(f.Depth, f.ExceptionType, f.Message, f.ErrorCode))],
                new CrashDumpContextDto(
                    snapshot.ExecutionContext.OperationName,
                    snapshot.ExecutionContext.ToolName,
                    snapshot.ExecutionContext.TurnIndex,
                    snapshot.ExecutionContext.RequestId,
                    Environment.ProcessId
                )
            );
            SafeFileIO.WriteAllText(jsonPath, JsonSerializer.Serialize(dto, Cli.Output.CliOutputJsonContext.Default.CrashDumpSnapshotDto)).GetAwaiter().GetResult();  // style-allow: sync-block — CLI 崩溃转储同步收尾: 进程退出前同步落盘
        } catch (Exception jsonEx) { Diag.WriteError("[CrashDump] 写入 JSON 快照失败", jsonEx); }

        // 2. 人类可读文本快照
        try {
            var txtPath = System.IO.Path.Combine(dumpDir, $"crash_{timestamp}_{idShort}.log");
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

            if (snapshot.ExceptionChain.Depth > 1) {
                txt.AppendLine($"异常链 (深度 {snapshot.ExceptionChain.Depth}):");
                foreach (var frame in snapshot.ExceptionChain.Frames)
                    txt.AppendLine($"  [{frame.Depth}] {frame.ExceptionType}: {frame.Message}");
                txt.AppendLine();
            }

            SafeFileIO.WriteAllText(txtPath, txt.ToString()).GetAwaiter().GetResult();  // style-allow: sync-block — CLI 崩溃转储同步收尾: 进程退出前同步落盘
        } catch (Exception txtEx) { Diag.WriteError("[CrashDump] 写入文本快照失败", txtEx); }

        // 3. stderr 输出（仅 UnhandledException，避免 pipe 阻塞）
        if (source == "UnhandledException") {
            try {
                Console.Error.WriteLine($"[CRASH] {snapshot.ExceptionType}: {snapshot.ExceptionMessage}");
                Console.Error.WriteLine($"[CRASH] 快照已保存到 {dumpDir}");
            } catch (Exception stderrEx) { Diag.WriteError("[CrashDump] stderr 输出失败", stderrEx); }
        }
    }

    /// <summary>
    /// 扫描 args 找第一个子命令的索引（允许全局选项在前，如 --trust --model &lt;model-id&gt; mcp_search read）。
    /// </summary>
    private static int? FindSubCommandIndex(string[] args) {
        for (var i = 0; i < args.Length; i++) {
            if (App.Builder.ApplicationBuilder.IsSubCommand(args[i]))
                return i;
        }
        return null;
    }

    /// <summary>
    /// 将子命令移到 args[0]，丢弃子命令前的全局选项（子命令执行器内部自行设置 TrustWorkspace 等）。
    /// 保留子命令及其后的所有参数（子命令专属选项如 --json --args-file 不受影响）。
    /// </summary>
    private static string[] ReorderSubCommandToFront(string[] args, int subCmdIndex) {
        if (subCmdIndex == 0)
            return args;
        var newArgs = new string[args.Length - subCmdIndex];
        newArgs[0] = args[subCmdIndex];
        if (subCmdIndex + 1 < args.Length)
            Array.Copy(args, subCmdIndex + 1, newArgs, 1, args.Length - subCmdIndex - 1);
        return newArgs;
    }

    /// <summary>
    /// 从 args 中提取 -h/--help 后的 topic 参数（支持多级，如 "scenario desktop"） — 用于多级渐进式展开帮助
    /// </summary>
    private static string? GetHelpTopic(string[] args) {
        for (var i = 0; i < args.Length - 1; i++) {
            if (args[i] is "-h" or "--help") {
                var parts = new List<string>();
                for (var j = i + 1; j < args.Length; j++) {
                    if (args[j].StartsWith('-')) break;
                    parts.Add(args[j]);
                }
                return parts.Count > 0 ? string.Join(' ', parts) : null;
            }
        }
        return null;
    }

    /// <summary>
    /// ADR 0155: 注册当前 jcc 实例到 Hub 服务端 — 自动注册+心跳+SSE消息接收。
    /// 退出时通过 DisposeAsync 自动注销。
    /// </summary>
    private static async Task<Api.Chat.HubAgentClient?> RegisterHubAgentAsync() {
        if (App.Builder.EngineApplicationBuilder.ShouldUseDirectEndpoint())
            return null;

        try {
            using var healthClient = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
            var healthResponse = await healthClient.GetAsync("http://localhost:3457/health").ConfigureAwait(false);
            if (!healthResponse.IsSuccessStatusCode) return null;

            var agentId = Api.Chat.HubAgentClient.GenerateAgentId();
            var agent = new Api.Chat.HubAgentClient(agentId, capabilities: []);
            var registered = await agent.RegisterAsync().ConfigureAwait(false);
            if (!registered) return null;

            agent.StartBackgroundTasks();
            if (Diag.IsDebugLog)
                Console.Error.WriteLine($"[HUB] Agent 已注册: {agentId}");
            return agent;
        } catch (Exception ex) {
            // Hub 没起 / 握手超时属于常见形态（-p 单次调用根本不需要 Hub），
            // 由上层在「真需要 Hub 却不可达」时给出可行动的错误，这里不再重复喊。
            Diag.WriteLine($"[HUB] Agent 注册失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 查找 jcchub.exe — 发布布局与 jcc 同目录；开发布局在 artifacts/bin/Hub/{Config}/{TFM}/。
    /// </summary>
    private static string? FindHubExecutable() {
        var baseDir = AppContext.BaseDirectory;
        var sameDir = Path.Combine(baseDir, "jcchub.exe");
        if (File.Exists(sameDir))
            return sameDir;

        var tfmDir = new DirectoryInfo(baseDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (tfmDir.Parent?.Parent?.Parent is not { } binRoot
            || !string.Equals(tfmDir.Parent.Parent.Name, "JoinCode", StringComparison.OrdinalIgnoreCase))
            return null;

        foreach (var config in new[] { tfmDir.Parent.Name, "Debug", "Release" }.Distinct(StringComparer.OrdinalIgnoreCase)) {
            var candidate = Path.Combine(binRoot.FullName, "Hub", config, tfmDir.Name, "jcchub.exe");
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    /// <summary>
    /// ADR 0155: 确保 jcchub.exe 已启动 — 检测端口 3457，没有服务则拉起独立进程。
    /// 固定端口 3457，不需要环境变量控制。一切默认。
    /// </summary>
    private static async Task EnsureHubServerAsync() {
        if (App.Builder.EngineApplicationBuilder.ShouldUseDirectEndpoint())
            return;

        const string hubEndpoint = "http://localhost:3457";

        if (await IsHubServerRunningAsync(hubEndpoint).ConfigureAwait(false))
            return;

        var hubExePath = FindHubExecutable();
        if (hubExePath is null) {
            // 同上：没有 jcchub.exe 不是错误，只有「需要 Hub 又连不上」时才由上层报错并给指引
            Diag.WriteLine($"[HUB] jcchub.exe 未找到（已查找同目录与开发 artifacts/Hub 布局）: {AppContext.BaseDirectory}");
            return;
        }

        try {
            var psi = new ProcessStartInfo {
                FileName = hubExePath,
                WorkingDirectory = Path.GetDirectoryName(hubExePath) ?? AppContext.BaseDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(psi);
            if (process is null) {
                Console.Error.WriteLine("[HUB] 无法启动 jcchub.exe");
                return;
            }

            for (var i = 0; i < 30; i++) {
                await Task.Delay(200).ConfigureAwait(false);
                if (await IsHubServerRunningAsync(hubEndpoint).ConfigureAwait(false)) {
                    Console.Error.WriteLine($"[HUB] AgentHubServer 已启动 — {hubEndpoint}");
                    return;
                }
            }

            Console.Error.WriteLine("[HUB] jcchub.exe 启动超时（6s），继续执行");
        } catch (Exception ex) {
            Console.Error.WriteLine($"[HUB] 启动 jcchub.exe 失败: {ex.Message}");
        }
    }

    private static async Task<bool> IsHubServerRunningAsync(string endpoint) {
        try {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
            var response = await client.GetAsync($"{endpoint}/health").ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        } catch {
            return false;
        }
    }
}

/// <summary>崩溃快照 JSON DTO — 供 JsonSerializer 序列化。</summary>
public sealed record CrashDumpSnapshotDto(
    Guid Id,
    DateTimeOffset CapturedAt,
    string FenceName,
    string Severity,
    string ExceptionType,
    string ExceptionMessage,
    string? ErrorCode,
    string? StackTrace,
    string Source,
    CrashDumpFrameDto[] ExceptionChain,
    CrashDumpContextDto ExecutionContext
);

/// <summary>异常链帧 DTO。</summary>
public sealed record CrashDumpFrameDto(int Depth, string ExceptionType, string Message, string? ErrorCode);

/// <summary>执行上下文 DTO。</summary>
public sealed record CrashDumpContextDto(string? OperationName, string? ToolName, int? TurnIndex, string? RequestId, int ProcessId);