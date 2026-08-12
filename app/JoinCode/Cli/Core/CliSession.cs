namespace JoinCode.Cli;

/// <summary>
/// 纯 CLI 会话 — 替代 TuiSession，使用纯控制台 I/O
/// </summary>
public sealed class CliSession
{
    private readonly ICodeService _codeService;
    private readonly IPlanService _planService;
    private readonly IToolRegistry _toolRegistry;
    private readonly IFileSystem _fs;
    private readonly CliServiceContext? _optionalServices;
    private readonly ChatCommandRegistry _commandRegistry;
    private readonly TurnDiffService _turnDiffService = new();
    private readonly DateTime _sessionStartedAt;
    private readonly SessionController _controller;
    private readonly IClockService _clock;
    private readonly ILogger<CliSession>? _logger;
    private readonly CommandServices _commandServices;
    private readonly ICmdMap _cmdMap;

    /// <summary>
    /// 会话实体 — 派生自 Entity，自动注册到 Session.Registry + ObjectIdManager
    /// </summary>
    private readonly Session _sessionEntity;

    /// <summary>
    /// 会话 ObjectId — 全局唯一标识，跨域引用用此
    /// </summary>
    private readonly ObjectId _sessionObjectId;

    /// <summary>
    /// 会话 ID 字符串 — 用于持久化隔离，可被 OverrideSessionId 覆盖以恢复原会话
    /// </summary>
    private string _sessionId;

    /// <summary>会话是否正在运行</summary>
    public bool IsRunning { get; private set; } = true;

    /// <summary>最后一次响应文本</summary>
    public string LastResponse { get; private set; } = string.Empty;

    /// <summary>聊天服务</summary>
    public IChatService ChatService => _controller.ChatService;

    /// <summary>DI 服务提供者</summary>
    public IServiceProvider? ServiceProvider => _optionalServices?.ServiceProvider;

    public CliSession(
        IChatService chatService,
        ICodeService codeService,
        IPlanService planService,
        IToolRegistry toolRegistry,
        IFileSystem fs,
        CliServiceContext? optionalServices = null,
        IClockService? clock = null,
        ILogger<CliSession>? logger = null)
    {
        _clock = clock ?? SystemClockService.Instance;
        _logger = logger;
        _sessionStartedAt = _clock.GetUtcNow();
        _codeService = codeService;
        _planService = planService;
        _toolRegistry = toolRegistry;
        _fs = fs;
        _optionalServices = optionalServices;
        _commandRegistry = new ChatCommandRegistry();
        GeneratedCommandRegistration.RegisterAllChatCommands(_commandRegistry);

        _sessionEntity = new Session();
        _sessionObjectId = _sessionEntity.ObjectId;
        _sessionId = _sessionObjectId.UniqueId;

        _optionalServices?.GoalEngine?.SetSessionId(_sessionId);
        _optionalServices?.GoalRegistry?.SetSessionId(_sessionId);
        _optionalServices?.CronTaskStore?.SetSessionId(_sessionId);
        _optionalServices?.DreamTaskRegistry?.SetSessionId(_sessionId);

        _controller = new SessionController(
            chatService,
            new CliEventConsumer(),
            _turnDiffService,
            _sessionId,
            optionalServices?.ServiceProvider);

        _commandServices = new CommandServices
        {
            ChatService = chatService,
            CodeService = _codeService,
            PlanService = _planService,
            ServiceProvider = optionalServices?.ServiceProvider,
            ToolRegistry = _toolRegistry,
            CommandRegistry = _commandRegistry,
            GoalEngine = optionalServices?.GoalEngine,
            GoalRegistry = optionalServices?.GoalRegistry,
            CronTaskStore = optionalServices?.CronTaskStore,
            SimpleModeService = optionalServices?.SimpleModeService,
            BriefModeService = optionalServices?.BriefModeService ?? optionalServices?.ServiceProvider?.GetService<IBriefModeService>(),
            HookConfigurationManager = optionalServices?.HookConfigurationManager,
            PluginManager = optionalServices?.PluginManager,
            BridgeClient = optionalServices?.BridgeClient,
            WorkflowConfig = optionalServices?.WorkflowConfig,
            ExecutionSettingsProvider = optionalServices?.ExecutionSettingsProvider,
            MemoryManagementService = optionalServices?.MemoryManagementService,
            TaskService = optionalServices?.TaskService,
            TodoService = optionalServices?.TodoService,
            UsageTracker = optionalServices?.UsageTracker,
            PermissionManager = optionalServices?.PermissionManager,
            ThinkingStore = optionalServices?.ThinkingStore ?? optionalServices?.ServiceProvider?.GetService<IThinkingStore>(),
            RateLimitTracker = optionalServices?.RateLimitTracker ?? optionalServices?.ServiceProvider?.GetService<IRateLimitTracker>(),
            WorkflowTaskExecutor = optionalServices?.WorkflowTaskExecutor ?? optionalServices?.ServiceProvider?.GetService<IWorkflowTaskExecutor>(),
            CostTracker = optionalServices?.ServiceProvider?.GetService<Core.CostTracking.CostTracker>(),
            TokenStorage = optionalServices?.ServiceProvider?.GetService<ITokenStorage>(),
            PkceGenerator = optionalServices?.ServiceProvider?.GetService<IPkceGenerator>(),
            WorktreeService = optionalServices?.ServiceProvider?.GetService<IAgentWorktreeService>(),
            ClipboardService = optionalServices?.ClipboardService ?? optionalServices?.ServiceProvider?.GetService<IClipboardService>(),
            WorkspaceService = optionalServices?.WorkspaceService ?? optionalServices?.ServiceProvider?.GetService<IWorkspaceService>(),
            FileOperationTracker = optionalServices?.FileOperationTracker ?? optionalServices?.ServiceProvider?.GetService<IFileOperationTracker>(),
            TurnDiffProvider = _turnDiffService,
            SessionTagService = optionalServices?.SessionTagService ?? optionalServices?.ServiceProvider?.GetService<ISessionTagService>(),
            WebService = optionalServices?.ServiceProvider?.GetService<IWebService>(),
            FileSystem = _fs,
        };

        _cmdMap = new CmdMap(_commandRegistry, _toolRegistry);
    }

    /// <summary>
    /// 覆盖会话 ID — 恢复历史会话时调用，将 sessionId 设为原会话 ID，
    /// 同步更新 GoalEngine/GoalRegistry 的会话隔离标识，使持久化状态关联到原会话。
    /// </summary>
    public void OverrideSessionId(string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        _sessionId = sessionId;
        _optionalServices?.GoalEngine?.SetSessionId(sessionId);
        _optionalServices?.GoalRegistry?.SetSessionId(sessionId);
        _optionalServices?.CronTaskStore?.SetSessionId(sessionId);
        _optionalServices?.DreamTaskRegistry?.SetSessionId(sessionId);
    }

    /// <summary>
    /// 初始化 — 加载自定义命令
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var loader = new CustomCommandLoader(_fs);
        var workingDir = _fs.GetCurrentDirectory();
        var projectCommands = await loader.LoadProjectCommandsAsync(workingDir, cancellationToken).ConfigureAwait(false);
        var userCommands = await loader.LoadUserCommandsAsync(cancellationToken).ConfigureAwait(false);
        foreach (var cmd in projectCommands) _commandRegistry.Register(new CustomChatCommand(cmd));
        foreach (var cmd in userCommands) _commandRegistry.Register(new CustomChatCommand(cmd));
    }

    /// <summary>
    /// 获取所有已注册命令的信息
    /// </summary>
    public IEnumerable<ChatCommandInfo> GetCommandInfos() => _commandRegistry.GetCommandInfos();

    /// <summary>
    /// 停止会话
    /// </summary>
    public void Stop() => IsRunning = false;

    /// <summary>
    /// 处理用户输入 — 命令或聊天消息
    /// </summary>
    public async Task ProcessUserInputAsync(string input, CancellationToken cancellationToken = default)
    {
        if (!IsRunning) return;
        if (string.IsNullOrWhiteSpace(input)) return;

        // 去除 BOM 前缀（PowerShell 管道 UTF-8 可能带 BOM）
        if (input.Length > 0 && input[0] == '\uFEFF')
            input = input[1..];

        var telemetry = _optionalServices?.ServiceProvider?.GetService<ITelemetryService>();
        var span = telemetry?.StartSpan(
            input.StartsWith('/') ? $"cli.command{input.Split(' ')[0]}" : "cli.chat",
            TelemetrySpanKind.Server);
        span?.SetTag("input.length", input.Length);
        span?.SetTag("session.id", _sessionId);

        try
        {
            if (input.StartsWith('/'))
            {
                await HandleCommandAsync(input, cancellationToken);
            }
            else
            {
                _turnDiffService.RecordUserPrompt(input);
                await StreamResponseAsync(input, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            span?.SetTag("error", true);
            span?.SetTag("error.message", ex.Message);
            throw;
        }
    }

    private async Task HandleCommandAsync(string input, CancellationToken cancellationToken)
    {
        Diag.WriteLifecycle($"[DIAG-CLI] HandleCommandAsync entry: input='{(input.Length > 60 ? input[..60] + "..." : input)}'");
        var parseResult = _commandRegistry.Parse(input);
        if (!parseResult.IsSuccess)
        {
            Diag.WriteLifecycle($"[DIAG-CLI] Parse FAILED for: '{input}', error={parseResult.ErrorMessage}");
            return;
        }

        Diag.WriteLifecycle($"[DIAG-CLI] Parse OK, commandName={parseResult.CommandName}, arguments={parseResult.Arguments}");
        var descriptor = await _cmdMap.ResolveAsync(parseResult.CommandName ?? throw new InvalidOperationException("CommandName should not be null after successful parse"), cancellationToken).ConfigureAwait(false);
        var command = descriptor?.SlashCommand;
        if (command == null)
        {
            var allCommands = _commandRegistry.GetAllCommands().Keys.OrderBy(k => k).ToArray();
            Diag.WriteLifecycle($"[DIAG-CLI] Command NOT FOUND: '{parseResult.CommandName}', registeredCount={allCommands.Length}, registered=[{string.Join(", ", allCommands)}]");
            ShowUnknownCommandHelp();
            return;
        }

        Diag.WriteLifecycle($"[DIAG-CLI] Command resolved: name={command.Name}, type={command.GetType().FullName}");

        var context = new ChatCommandContext
        {
            Arguments = parseResult.Arguments,
            CancellationToken = cancellationToken,
            SessionStartedAt = _sessionStartedAt,
            SessionId = _sessionId,
            Services = new CommandServiceProvider(_commandServices, _optionalServices?.ServiceProvider),
            ClearScreen = () =>
            {
                try { System.Console.Clear(); } catch (IOException ex) { _logger?.LogWarning(ex, "清屏失败"); }
            },
            Confirm = msg =>
            {
                if (Core.Utils.TestEnvironmentDetector.IsNonInteractive) return false;
                TerminalHelper.WriteRaw(msg + " (y/N) ");
                var response = TerminalHelper.ReadLine();
                return response?.ToLowerInvariant() == "y";
            },
            Prompt = msg =>
            {
                if (Core.Utils.TestEnvironmentDetector.IsNonInteractive) return null;
                TerminalHelper.WriteRaw(msg);
                return TerminalHelper.ReadLine();
            },
            ReadPassword = prompt =>
            {
                if (Core.Utils.TestEnvironmentDetector.IsNonInteractive) return string.Empty;
                TerminalHelper.WriteRaw(prompt);
                var password = new StringBuilder();
                while (true)
                {
                    var key = TerminalHelper.ReadKey(true);
                    if (key.Key == ConsoleKey.Enter) break;
                    if (key.Key == ConsoleKey.Backspace)
                    {
                        if (password.Length > 0) password.Remove(password.Length - 1, 1);
                    }
                    else
                    {
                        password.Append(key.KeyChar);
                    }
                }
                TerminalHelper.NewLine();
                return password.ToString();
            }
        };

        var originalOut = TerminalHelper.Out;
        var commandOutput = new StringBuilder();
        using var commandWriter = new System.IO.StringWriter(commandOutput);
        TerminalHelper.SetOut(commandWriter);

        ChatCommandResult result;
        try
        {
            Diag.WriteLifecycle($"[DIAG-CLI] executing command '{command.Name}', args='{parseResult.Arguments}'");
            result = await command.ExecuteAsync(context);
            Diag.WriteLifecycle($"[DIAG-CLI] command '{command.Name}' returned: ShouldContinue={result.ShouldContinue}, resultType={result.GetType().FullName}");
        }
        catch (Exception ex)
        {
            Diag.WriteLifecycle($"[DIAG-CLI] command '{command.Name}' THREW {ex.GetType().Name}: {ex.Message}");
            throw;
        }
        finally
        {
            TerminalHelper.SetOut(originalOut);
            commandWriter.Flush();
        }

        var outputText = commandOutput.ToString();
        Diag.WriteLifecycle($"[DIAG-CLI] command '{command.Name}' outputLen={outputText.Length}, outputPreview='{(outputText.Length > 200 ? outputText[..200] + "..." : outputText)}'");
        if (!string.IsNullOrWhiteSpace(outputText))
        {
            TerminalHelper.WriteLine(outputText.TrimEnd());
        }

        if (!result.ShouldContinue)
        {
            Diag.WriteLifecycle($"[DIAG-CLI] command '{command.Name}' ShouldContinue=false, calling Stop()");
            Stop();
        }
    }

    private void ShowUnknownCommandHelp()
    {
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("未知命令。可用命令:");
        foreach (var info in _commandRegistry.GetCommandInfos())
        {
            sb.AppendLine($"  {info.Usage,-24} {info.Description}");
        }
        sb.AppendLine();
        TerminalHelper.WriteLine(sb.ToString().TrimEnd());
    }

    private async Task StreamResponseAsync(string input, CancellationToken cancellationToken)
    {
        Diag.WriteLine($"[CliSession] StreamResponseAsync entry: input='{input}'");
        var result = await _controller.StreamResponseAsync(input, cancellationToken).ConfigureAwait(false);

        if (result.Succeeded)
        {
            TerminalHelper.NewLine();
            LastResponse = result.Response;
            await AppendTranscriptEntriesAsync(input, LastResponse, result.RequestTimestamp, cancellationToken).ConfigureAwait(false);
        }
        else if (result.TimedOut)
        {
            Diag.WriteLine($"[CliSession] API timeout ({result.TimeoutMs}ms no response)");
            using var _ = TerminalHelper.SetColor(ConsoleColor.Yellow);
            TerminalHelper.WriteLine();
            TerminalHelper.WriteLine($"API 请求超时（{result.TimeoutMs / 1000}s 无响应）。请检查：");
            TerminalHelper.WriteLine("  1. 是否已配置 API Key");
            TerminalHelper.WriteLine("  2. 网络连接是否正常");
            TerminalHelper.WriteLine("  3. API 服务是否可用");
        }
        else if (result.WasCancelled)
        {
            Diag.WriteLine("[CliSession] OperationCanceledException (cancelled)");
            LastResponse = result.Response;
            if (!string.IsNullOrEmpty(LastResponse))
            {
                TerminalHelper.NewLine();
            }
        }
        else
        {
            Diag.WriteLine($"[CliSession] Exception: {result.ErrorMessage}");
            if (!string.IsNullOrEmpty(result.ErrorCode))
            {
                TerminalHelper.WriteLine();
                TerminalHelper.WriteLine($"✖ {result.ErrorMessage}");
                if (result.IsRetryable)
                    TerminalHelper.WriteLine("  此错误通常可重试，请稍后再试。");
                TerminalHelper.WriteLine("  请检查：1. API Key 配置  2. 网络连接  3. API 服务状态");
            }
            else
            {
                TerminalHelper.WriteLine($"错误: {result.ErrorMessage}");
            }
            LastResponse = result.Response;
        }
        Diag.WriteLine($"[CliSession] StreamResponseAsync done: succeeded={result.Succeeded}, responseLen={result.Response.Length}");
    }

    private async Task AppendTranscriptEntriesAsync(string userInput, string assistantResponse, DateTime timestamp, CancellationToken cancellationToken)
    {
        if (_optionalServices?.TranscriptService is null) return;

        // Worker 进程不写主 session 文件 — 只写 AgentTranscriptService 的独立文件
        var agentRole = Environment.GetEnvironmentVariable(JccEnvVar.AgentRole.ToValue());
        if (string.Equals(agentRole, "worker", StringComparison.OrdinalIgnoreCase)) return;

        try
        {
            var entries = new TranscriptEntry[]
            {
                new() { SessionId = _sessionId, Role = "user", Content = userInput, Timestamp = timestamp },
                new() { SessionId = _sessionId, Role = "assistant", Content = assistantResponse, Timestamp = _clock.GetUtcNow() }
            };
            await _optionalServices.TranscriptService.AppendEntriesAsync(_sessionId, entries, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Transcript 写入失败");
        }
    }

    private PermissionConfirmResult ShowPermissionConfirmation(PermissionPendingConfirmationException permEx)
    {
        TerminalHelper.WriteLine();
        TerminalHelper.WriteLine($"权限确认: {permEx.ConfirmationPrompt}");
        TerminalHelper.WriteRaw("(y)允许 / (a)始终允许 / (n)拒绝 [n]: ");

        if (TerminalHelper.IsInputRedirected || Core.Utils.TestEnvironmentDetector.IsNonInteractive)
        {
            TerminalHelper.WriteLine(L.T(StringKey.NonInteractivePermissionDenied));
            return PermissionConfirmResult.Deny;
        }

        try
        {
            var key = TerminalHelper.ReadKey(true);
            TerminalHelper.NewLine();
            return key.KeyChar switch
            {
                'y' or 'Y' => PermissionConfirmResult.Allow,
                'a' or 'A' => PermissionConfirmResult.AlwaysAllow,
                _ => PermissionConfirmResult.Deny
            };
        }
        catch
        {
            return PermissionConfirmResult.Deny;
        }
    }
}

/// <summary>
/// 权限确认结果
/// </summary>
internal enum PermissionConfirmResult
{
    Deny,
    Allow,
    AlwaysAllow
}
