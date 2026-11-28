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
                Diag.WriteLine("[RUN] NDJSON stream mode enabled (--format ndjson / --output-format stream-json)");
            }
            Cli.TerminalHelper.SuppressStdout = true; // Bug 12: 抑制 stdout 信息输出，确保只有结构化输出
        }

        var sp = host.Services;

        // TASK138 — SDK 流式输入：stdin 每行一条 user 消息 = 一次查询，初始化只跑一次，回合循环复用会话
        if (options.IsStreamJsonInputMode) {
            Diag.WriteLine("[RUN] stream-json input mode enabled (--input-format stream-json)");
            var streamExit = await RunStreamJsonInputAsync(context, sp, CancellationToken.None).ConfigureAwait(false);
            Cli.TerminalHelper.SuppressStdout = false; // 恢复 stdout（测试可重入）
            return streamExit;
        }

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
            .OnError(OnPipelineError)
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
            // NDJSON 模式: 事件已由 CliEventConsumer 流式输出，此处输出最终 result 消息（对齐基准 SDK result message）
            WriteResultMessage(context);
        }

        Cli.TerminalHelper.SuppressStdout = false; // 恢复 stdout（测试可重入）
        return context.ExitCode;
    }

    /// <summary>
    /// SDK 流式输入主循环（TASK138，<c>--input-format stream-json</c>）：
    /// 启动管道（日志/API Key/会话初始化/会话恢复/系统提示词）只跑一次，之后 stdin 每行 user 消息驱动一次
    /// <see cref="NonInteractiveExecuteStep"/>，每回合结束输出一条 <c>result</c>（带 query_id）；
    /// EOF 或取消即收尾。单回合失败不终止进程（SDK 侧按 result.is_error 判定），
    /// 进程退出码 = 至少有一回合成功则 0，否则最后一回合的退出码。
    /// </summary>
    private static async Task<int> RunStreamJsonInputAsync(StartupContext context, IServiceProvider sp, CancellationToken ct) {
        var initPipeline = new PipelineBuilder<StartupContext>()
            .Use(sp.GetRequiredService<StartupLoggingMiddleware>())
            .Use(sp.GetRequiredService<NonInteractiveApiKeyCheckStep>())
            .Use(sp.GetRequiredService<DebugDumpPromptStep>())
            .Use(sp.GetRequiredService<SessionInitStep>())
            .Use(sp.GetRequiredService<SessionResumeStep>())
            .Use(sp.GetRequiredService<SystemPromptApplyStep>())
            .Use(sp.GetRequiredService<InitDebugDumpStep>())
            .OnError(OnPipelineError)
            .Build();
        var turnPipeline = new PipelineBuilder<StartupContext>()
            .Use(sp.GetRequiredService<NonInteractiveExecuteStep>())
            .OnError(OnPipelineError)
            .Build();
        var cleanupPipeline = new PipelineBuilder<StartupContext>()
            .Use(sp.GetRequiredService<NonInteractiveExitCleanupStep>())
            .OnError(OnPipelineError)
            .Build();

        await initPipeline.ExecuteAsync(context, ct).ConfigureAwait(false);
        if (context.ExitCode != 0) {
            // 初始化失败（缺 API Key / 会话建不起来）→ 与单发模式同样直接退出，不进回合循环
            Diag.WriteLine($"[RUN] stream-json init failed, exitCode={context.ExitCode}");
            WriteResultMessage(context);
            return context.ExitCode;
        }

        using var stdin = new StreamReader(Console.OpenStandardInput(), System.Text.Encoding.UTF8);
        using var turnCancellation = new TurnCancellation();
        // TASK143：权限模式应用器 — 与 bridge_serve 共用同一份解析/应用/失败文案。
        // 解析失败（引擎未注册 IPermissionChecker）时为 null，控制行会回 error 而不是静默成功。
        var permissionModeApplier = PermissionModeApplierFactory.Create(
            await PermissionModeApplierFactory.ResolveAsync(sp).ConfigureAwait(false));
        // 有界邮箱：SDK 客户端连发多条时背压到 stdin 读取，避免无限堆积（S5 门禁亦禁止 unbounded）。
        var lines = System.Threading.Channels.Channel.CreateBounded<Cli.Output.SdkStreamInputLine>(
            new System.Threading.Channels.BoundedChannelOptions(256) {
                SingleReader = true,
                SingleWriter = true,
                FullMode = System.Threading.Channels.BoundedChannelFullMode.Wait,
            });

        // TASK142-B：stdin 读取必须独立于回合执行 —— 否则 interrupt 要等当前回合跑完才被读到，
        // "打断在途查询"就成了空话。控制行在生产端直接取消当前 CTS 并回 control_response；
        // 用户行入 Channel，由消费端逐回合串行执行（SDK 的 batch 语义不在此引入并发）。
        var readerTask = Task.Run(async () => {
            try {
                while (!ct.IsCancellationRequested) {
                    var line = await stdin.ReadLineAsync(ct).ConfigureAwait(false);
                    if (line is null)
                        break;

                    var parsed = Cli.Output.SdkStreamInputParser.Interpret(line);
                    // 拒绝优先：带 request_id 的不合法/未实现指令必须回 error，否则客户端永久等待。
                    if (parsed.ControlRejection is { } rejection && parsed.ControlRequestId is { } rejectedId) {
                        Console.Error.WriteLine($"[INPUT] {rejection}");
                        WriteControlResponse(Cli.Output.SdkStreamControlResponse.Failure(rejectedId, rejection));
                        Diag.WriteLine($"[RUN] control request {rejectedId} rejected");
                        continue;
                    }
                    if (parsed.IsInterrupt && parsed.ControlRequestId is { } interruptId) {
                        turnCancellation.CancelActive();
                        WriteControlResponse(Cli.Output.SdkStreamControlResponse.Success(interruptId));
                        Diag.WriteLine($"[RUN] control_response sent for {interruptId}");
                        continue;
                    }
                    if (parsed.RequestedPermissionMode is { } requestedMode && parsed.ControlRequestId is { } pmId) {
                        ApplyPermissionMode(pmId, requestedMode, permissionModeApplier);
                        continue;
                    }
                    if (parsed.Diagnostic is { } diagnostic) {
                        // 诊断走 stderr：stdout 是纯协议流
                        Console.Error.WriteLine(diagnostic);
                        continue;
                    }
                    if (parsed.Text is null)
                        continue;
                    await lines.Writer.WriteAsync(parsed, ct).ConfigureAwait(false);
                }
            } catch (OperationCanceledException) {
                // 正常收尾
            } finally {
                lines.Writer.TryComplete();
            }
        }, ct);

        var sawSuccess = false;
        var lastExit = 0;
        await foreach (var parsed in lines.Reader.ReadAllAsync(ct).ConfigureAwait(false)) {
            if (parsed.Text is not { } text)
                continue;

            // TASK142-A：bridge 父进程靠 stdout 的 type=user 行派生会话标题（OnFirstUserMessage）。
            // 回显必须是「真人消息」形状 —— 不能带 isReplay（基准侧 extractUserMessageText 会跳过它）。
            if (context.Options.ReplayUserMessages)
                WriteReplayUserMessage(context, text);

            ResetTurnState(context, text);
            using var turnCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            turnCancellation.SetActive(turnCts);
            Diag.WriteLine("[RUN] stream-json turn start");
            try {
                await turnPipeline.ExecuteAsync(context, turnCts.Token).ConfigureAwait(false);
            } catch (OperationCanceledException) when (turnCts.IsCancellationRequested) {
                context.StructuredError = new Cli.Output.CliStructuredError(
                    "INTERRUPTED", "查询已被 SDK control_request/interrupt 取消",
                    hint: "确认取消后可用 type=user 提交下一回合", retryable: true);
                context.ExitCode = (int)ExitCode.GeneralError;
            } finally {
                turnCancellation.ClearActive(turnCts);
            }
            WriteResultMessage(context);
            lastExit = context.ExitCode;
            sawSuccess |= context.ExitCode == 0;
        }

        await readerTask.ConfigureAwait(false);
        await cleanupPipeline.ExecuteAsync(context, ct).ConfigureAwait(false);
        Diag.WriteLine($"[RUN] stream-json loop done, sawSuccess={sawSuccess}, lastExit={lastExit}");
        return sawSuccess ? 0 : lastExit;
    }

    /// <summary>
    /// 回显一条真实用户消息到 stdout（TASK142-A，<c>--replay-user-messages</c>）。
    /// <para>形状固定为 <c>type=user, message.role=user, message.content=[{type:"text",text:…}]</c>，
    /// 不写 <c>isReplay</c> —— bridge 父进程的 <c>ExtractUserMessageText</c> 与基准侧一致，
    /// 遇到 isReplay / isSynthetic / parent_tool_use_id 会判定为「不是真人输入」而跳过。</para>
    /// </summary>
    /// <param name="context">启动上下文（取 session_id）</param>
    /// <param name="text">本回合用户文本</param>
    private static void WriteReplayUserMessage(StartupContext context, string text) {
        var echo = Cli.Output.SdkUserMessage.Create(
            new Cli.Output.SdkMessageBody {
                Role = "user",
                Content = [new Cli.Output.SdkTextBlock { Text = text }],
            },
            context.SessionId);
        Console.WriteLine(RelaxedJsonSerializer.Serialize(echo, Cli.Output.CliOutputJsonContext.Default));
    }

    /// <summary>回合间复位启动上下文（TASK138）— 每回合独立的输入、耗时、用量、错误与查询身份。</summary>
    /// <param name="context">启动上下文（会话与配置跨回合保留）。</param>
    /// <param name="prompt">本回合用户输入。</param>
    private static void ResetTurnState(StartupContext context, string prompt) {
        context.NonInteractivePrompt = prompt;
        context.ExitCode = 0;
        context.StructuredError = null;
        context.FullResponse = null;
        context.ElapsedMs = null;
        context.ApiDurationMs = 0;
        context.NumTurns = 0;
        context.TotalCostUsd = 0;
        context.StopReason = null;
        context.StructuredOutput = null;
        context.Usage = null;
        context.QueryId = null;
        context.PermissionDenials.Clear();
    }

    /// <summary>
    /// 输出 NDJSON 结果消息（对齐基准 SDK result message）— 单发与流式输入共用；
    /// TASK137 — 带 <c>query_id</c>（失败路径同样有，查询身份在回合开始即生成）。
    /// </summary>
    private static void WriteResultMessage(StartupContext context) {
        var ok = context.ExitCode == 0;
        var resultMessage = Cli.Output.SdkResultMessage.Create(context.SessionId) with {
            QueryId = context.QueryId,
            Subtype = ok ? "success" : "error_during_execution",
            DurationMs = context.ElapsedMs ?? 0,
            DurationApiMs = context.ApiDurationMs,
            IsError = !ok,
            Result = ok ? (context.FullResponse ?? string.Empty) : null,
            NumTurns = context.NumTurns,
            TotalCostUsd = context.TotalCostUsd,
            StopReason = context.StopReason,
            StructuredOutput = ok ? context.StructuredOutput : null,
            Usage = context.Usage,
            ModelUsage = BuildModelUsage(context),
            PermissionDenials = context.PermissionDenials,
            Errors = ok ? [] : [context.StructuredError?.Message ?? $"Exit code {context.ExitCode}"],
        };
        var json = RelaxedJsonSerializer.Serialize(resultMessage, Cli.Output.CliOutputJsonContext.Default);
        Console.WriteLine(json);
    }

    /// <summary>管道异常兜底 — 翻译为结构化错误并落退出码（单发与流式输入共用同一策略）。</summary>
    private static void OnPipelineError(StartupContext ctx, Exception ex) {
        Diag.WriteLine($"[RUN] OnError: {ex.GetType().Name}: {ex.Message}");
        ctx.StructuredError = new Cli.Output.CliStructuredError(
            "RUNTIME_ERROR", ex.Message,
            hint: "请检查错误日志获取详细信息",
            retryable: false);
        ctx.ExitCode = (int)ExitCode.GeneralError;
    }

    /// <summary>
    /// 应用 <c>set_permission_mode</c> 控制行（TASK143）— 成功回实际生效模式（bypass 可能被安全闸拦成别的值），
    /// 失败回协议 error 分支（含合法值清单）。不中断回合循环。
    /// </summary>
    /// <param name="requestId">控制请求 ID</param>
    /// <param name="mode">请求的权限模式（协议字符串）</param>
    /// <param name="applier">权限模式应用器（null = 引擎无权限系统）</param>
    private static void ApplyPermissionMode(
        string requestId,
        string mode,
        CliCommands.BridgeServeLoop.PermissionModeApplier? applier) {
        if (applier?.Invoke(mode) is { } effectiveMode) {
            WriteControlResponse(Cli.Output.SdkStreamControlResponse.Success(requestId, effectiveMode.ToValue()));
            Diag.WriteLine($"[RUN] permission mode '{mode}' applied as '{effectiveMode.ToValue()}' for {requestId}");
            return;
        }

        // 确定性失败：不回 success —— 否则客户端以为权限已放宽，后续工具调用被拒时无法归因。
        var failure = PermissionModeApplierFactory.DescribeFailure(mode);
        WriteControlResponse(Cli.Output.SdkStreamControlResponse.Failure(requestId, failure));
        Diag.WriteLine($"[RUN] permission mode '{mode}' rejected for {requestId}");
    }

    /// <summary>写一条控制应答（单次写 + LF，stdout 为纯协议流）。</summary>
    /// <param name="response">应答消息</param>
    private static void WriteControlResponse(Cli.Output.SdkStreamControlResponse response) {
        Console.WriteLine(RelaxedJsonSerializer.Serialize(response, Cli.Output.CliOutputJsonContext.Default));
    }

    /// <summary>
    /// 在途回合的取消句柄（TASK142-B）— 生产端（stdin 读循环）调 <see cref="CancelActive"/>，
    /// 消费端（回合循环）在回合开始/结束时登记与清除；用锁保护引用交接，避免 CTS 被释放时的竞态。
    /// </summary>
    internal sealed class TurnCancellation : IDisposable {
        /// <summary>不可变快照 — 一次 CAS 换整个句柄，锁内状态为零。</summary>
        private sealed record ActiveTurn(CancellationTokenSource Cts);

        private ActiveTurn? _active;
        private int _disposed;

        /// <summary>登记当前回合的 CTS。</summary>
        /// <param name="cts">回合 CTS</param>
        public void SetActive(CancellationTokenSource cts) {
            if (Volatile.Read(ref _disposed) != 0) {
                cts.Dispose();
                return;
            }
            Interlocked.Exchange(ref _active, new ActiveTurn(cts));
        }

        /// <summary>回合结束，仅当仍是同一个 CTS 才清除（避免清掉下一回合）。</summary>
        /// <param name="cts">回合 CTS</param>
        public void ClearActive(CancellationTokenSource cts) {
            var current = Volatile.Read(ref _active);
            if (current is not null && ReferenceEquals(current.Cts, cts))
                Interlocked.CompareExchange(ref _active, null, current);
        }

        /// <summary>取消当前回合；命中已收尾的 CTS 时记诊断后忽略。</summary>
        public void CancelActive() {
            try {
                Volatile.Read(ref _active)?.Cts.Cancel();
            } catch (ObjectDisposedException) {
                Diag.WriteLine("[RUN] interrupt 命中已收尾的回合 CTS，忽略");
            }
        }

        /// <summary>幂等释放仍登记的 CTS。</summary>
        public void Dispose() {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
            Interlocked.Exchange(ref _active, null)?.Cts.Dispose();
        }
    }

    /// <summary>从 Usage + TotalCostUsd 构造按模型分组的用量字典</summary>
    private static System.Collections.Generic.Dictionary<string, Cli.Output.SdkModelUsage> BuildModelUsage(StartupContext context) {
        var modelId = context.Config.Provider.ModelId;
        if (string.IsNullOrEmpty(modelId) || context.Usage is null)
            return new();
        return new() {
            [modelId] = new Cli.Output.SdkModelUsage {
                InputTokens = context.Usage.InputTokens,
                OutputTokens = context.Usage.OutputTokens,
                CacheReadInputTokens = context.Usage.CacheReadInputTokens ?? 0,
                CacheCreationInputTokens = context.Usage.CacheCreationInputTokens ?? 0,
                CostUSD = context.TotalCostUsd,
            }
        };
    }
}
