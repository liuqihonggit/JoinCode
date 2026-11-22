namespace Integration.Tests.McpTools;

/// <summary>
/// 隔离测试宿主 — 统一构造「InMemory 文件系统 + 独立临时目录」的 DI 容器。
/// <para>历史写法在每个测试类里 SetEnvironmentVariable(JCC_APP_DATA_FOLDER/JCC_FILE_SYSTEM_MODE)，
/// 但 (1) AppDataConstants._paths 类首次加载即固定，设环境变量后无人重调 FromEnvironment()，是空转；
/// (2) 环境变量是进程全局态，xUnit 类间并行会相互踩踏。</para>
/// <para>本类改为显式注入：<see cref="IFileSystem"/> 在 AddWorkflowServices 之后覆盖注册
/// （EnvSwitchRegistrar 用 AddSingleton，后注册 wins），临时目录随宿主实例传递，零全局状态。</para>
/// </summary>
public sealed class IsolatedTestHost : IAsyncDisposable {
    private readonly ServiceProvider _provider;

    /// <summary>本宿主独占的临时目录（patrol-{Guid:N}，绝不与其他测试共享）。</summary>
    public string TempDir { get; }

    /// <summary>宿主使用的内存文件系统。</summary>
    public IFileSystem FileSystem { get; }

    /// <summary>注入的 git 命令替身（仅 injectStubGitRunner=true 时非 null）。</summary>
    public StubGitCommandRunner? GitRunnerStub { get; }

    /// <summary>注入的 MCP 客户端替身（仅 injectFakeMcpClientFactory=true 时非 null）。</summary>
    public FakeMcpClient? McpClientStub { get; }

    /// <summary>
    /// 是否释放 DI 容器。冒烟测试会构造 LspManager，其 Dispose 走 LspInitActor.ShutdownAsync（Ask 等待），
    /// 在 xUnit 并行下偶发 10s 线程池饥饿超时（ActorAskDeadlockException，属已知 Actor Dispose 边界）。
    /// 这些宿主只持有内存对象（InMemoryFileSystem + DI），不释放不会泄漏真实资源，故默认 false 与原测试行为一致。
    /// </summary>
    private readonly bool _disposeProvider;

    /// <summary>构造隔离宿主。</summary>
    public IsolatedTestHost(
        WorkflowConfig? config = null,
        bool withTestPipelines = false,
        Action<IServiceCollection>? extraRegistration = null,
        LogLevel minLogLevel = LogLevel.Warning,
        bool disposeProvider = true,
        bool injectStubGitRunner = false,
        bool injectFakeMcpClientFactory = false) {
        _disposeProvider = disposeProvider;
        TempDir = Path.Combine(Path.GetTempPath(), $"patrol-{Guid.NewGuid():N}");

        var fileSystem = new IO.FileSystem.InMemoryFileSystem();
        fileSystem.CreateDirectory(TempDir);
        FileSystem = fileSystem;

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(minLogLevel));
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddWorkflowServices(config ?? new WorkflowConfig());
        if (withTestPipelines) services.AddTestPipelines();

        // 隔离宿主不注册 LLM 链；AutoClassifierMiddleware 的 LLM 阶段在生成式 DI 中是必填依赖，
        // 这里用规则分类器替身截断 ChatClient -> IQueryService 的重链路（生产容器仍走真实 Llm judge）。
        services.AddSingleton<ILlmAutoModeClassifier>(new RuleOnlyLlmAutoModeClassifierStub());

        // 后置覆盖：AddEnvSwitch 用 AddSingleton（后注册 wins），必须放在 AddWorkflowServices 之后。
        services.AddSingleton<IFileSystem>(fileSystem);
        if (injectStubGitRunner) {
            GitRunnerStub = new StubGitCommandRunner();
            services.AddSingleton<IGitCommandRunner>(GitRunnerStub);
        }
        if (injectFakeMcpClientFactory) {
            McpClientStub = new FakeMcpClient();
            services.AddSingleton<JoinCode.Abstractions.Mcp.Client.IMcpClientFactory>(
                new FakeMcpClientFactory(McpClientStub));
        }
        extraRegistration?.Invoke(services);

        _provider = services.BuildServiceProvider();
    }

    /// <summary>宿主 DI 容器 — 供按运行时 Type 解析（如反射扫描 handler）使用。</summary>
    public IServiceProvider Services => _provider;

    /// <summary>解析服务。</summary>
    public T GetRequiredService<T>() where T : notnull => _provider.GetRequiredService<T>();

    /// <summary>解析服务（可空）。</summary>
    public T? GetService<T>() where T : notnull => _provider.GetService<T>();

    /// <summary>异步初始化 MCP 服务。</summary>
    public async Task InitializeMcpAsync() {
        var mcp = _provider.GetRequiredService<IMcpService>();
        await mcp.InitializeAsync(_provider).ConfigureAwait(true);
    }

    public ValueTask DisposeAsync() =>
        _disposeProvider ? _provider.DisposeAsync() : ValueTask.CompletedTask;
}

/// <summary>
/// LLM 分类器测试替身 — 只走规则分类，不触碰 ChatClient / IQueryService。
/// 隔离宿主不装配 LLM；真实 LLM 灰区判定由 Guard.Security 单测与生产容器覆盖。
/// </summary>
public sealed class RuleOnlyLlmAutoModeClassifierStub : ILlmAutoModeClassifier {
    private readonly AutoModeClassifier _rules = new();

    /// <inheritdoc />
    public Task<ClassificationResult> ClassifyAsync(ClassificationRequest request, CancellationToken ct = default) =>
        _rules.ClassifyAsync(request, ct);
}

/// <summary>
/// Git 命令执行器测试替身 — 记录命令、返回成功空输出，零真实 git 副作用。
/// 用于巡检覆盖 git_/worktree 工具时验证「核心逻辑被真触发」而非被跳过。
/// </summary>
public sealed class StubGitCommandRunner : IGitCommandRunner {
    /// <summary>已执行命令（线程安全追加，xUnit 类间并行安全）。</summary>
    public System.Collections.Concurrent.ConcurrentBag<string> ExecutedCommands { get; } = [];

    public Task<GitCommandResult> ExecuteAsync(string arguments, string? workingDirectory = null, CancellationToken ct = default) {
        ExecutedCommands.Add(arguments);
        return Task.FromResult(new GitCommandResult {
            Success = true, Output = string.Empty, Error = string.Empty, ExitCode = 0,
        });
    }

    public Task<MergeConflictResult> DetectMergeConflictAsync(string branch1, string branch2, string? workingDirectory = null, CancellationToken ct = default)
        => Task.FromResult(new MergeConflictResult { HasConflict = false });

    public Task<StaleConflictMarkerResult> DetectStaleConflictMarkersAsync(string? workingDirectory = null, CancellationToken ct = default)
        => Task.FromResult(new StaleConflictMarkerResult { HasStaleMarkers = false });
}

/// <summary>
/// MCP 客户端测试替身 — 实现 IMcpClient 的最小可用语义（连接状态 + 空工具/资源列表），
/// 让 mcp_connect 走完整 handler 链路（校验/注册/状态保存）而不连真实服务器。
/// </summary>
public sealed class FakeMcpClient : JoinCode.Abstractions.Mcp.Client.IMcpClient {
    // 接口事件在此替身中无需触发（仅验证 mcp_connect 的连接链路，不验证通知）
#pragma warning disable CS0067
    public event EventHandler<JoinCode.Abstractions.Mcp.Client.McpNotificationReceivedEventArgs>? NotificationReceived;
    public event EventHandler<JoinCode.Abstractions.Mcp.Client.McpConnectionLostEventArgs>? ConnectionLost;
#pragma warning restore CS0067

    public int ConnectCount { get; private set; }
    public int DisconnectCount { get; private set; }
    public bool IsConnected { get; private set; }
    public JoinCode.Abstractions.Mcp.Protocol.Implementation? ServerInfo => null;
    public JoinCode.Abstractions.Mcp.Protocol.ServerCapabilities? ServerCapabilities => null;

    public void SetElicitationHandler(JoinCode.Abstractions.Mcp.Client.IElicitationHandler handler) { }
    public Task ConnectAsync(CancellationToken cancellationToken = default) {
        ConnectCount++;
        IsConnected = true;
        return Task.CompletedTask;
    }
    public Task DisconnectAsync(CancellationToken cancellationToken = default) {
        DisconnectCount++;
        IsConnected = false;
        return Task.CompletedTask;
    }
    public Task<JoinCode.Abstractions.Models.OperationResult<IReadOnlyList<ToolInfo>>> ListToolsAsync(CancellationToken ct = default)
        => Task.FromResult(JoinCode.Abstractions.Models.OperationResult<IReadOnlyList<ToolInfo>>.Ok([]));
    public Task<ToolResult> CallToolAsync(string toolName, Dictionary<string, JsonElement>? arguments = null, CancellationToken ct = default, JoinCode.Abstractions.Mcp.Client.McpProgressCallback? onProgress = null)
        => Task.FromResult(ToolResultBuilder.Success().WithText("fake mcp tool result").Build());
    public Task<JoinCode.Abstractions.Models.OperationResult<IReadOnlyList<JoinCode.Abstractions.Mcp.Protocol.McpResource>>> ListResourcesAsync(CancellationToken ct = default)
        => Task.FromResult(JoinCode.Abstractions.Models.OperationResult<IReadOnlyList<JoinCode.Abstractions.Mcp.Protocol.McpResource>>.Ok([]));
    public Task<JoinCode.Abstractions.Models.OperationResult<JoinCode.Abstractions.Mcp.Protocol.McpResourceContent?>> ReadResourceAsync(string uri, CancellationToken ct = default)
        => Task.FromResult(JoinCode.Abstractions.Models.OperationResult<JoinCode.Abstractions.Mcp.Protocol.McpResourceContent?>.Ok(null));
    public Task<JoinCode.Abstractions.Models.OperationResult<IReadOnlyList<JoinCode.Abstractions.Mcp.Protocol.McpPrompt>>> ListPromptsAsync(CancellationToken ct = default)
        => Task.FromResult(JoinCode.Abstractions.Models.OperationResult<IReadOnlyList<JoinCode.Abstractions.Mcp.Protocol.McpPrompt>>.Ok([]));
    public Task<JoinCode.Abstractions.Models.OperationResult<JoinCode.Abstractions.Mcp.Protocol.McpPromptMessage?>> GetPromptAsync(string name, Dictionary<string, JsonElement>? arguments = null, CancellationToken ct = default)
        => Task.FromResult(JoinCode.Abstractions.Models.OperationResult<JoinCode.Abstractions.Mcp.Protocol.McpPromptMessage?>.Ok(null));
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>MCP 客户端工厂替身 — 总返回同一个 FakeMcpClient。</summary>
public sealed class FakeMcpClientFactory : JoinCode.Abstractions.Mcp.Client.IMcpClientFactory {
    private readonly FakeMcpClient _client;
    public FakeMcpClientFactory(FakeMcpClient client) => _client = client;
    public JoinCode.Abstractions.Mcp.Client.IMcpClient CreateClient(JoinCode.Abstractions.Mcp.Client.McpServerConnectionConfig config, ILogger? logger = null) => _client;
    public JoinCode.Abstractions.Mcp.Client.IMcpClient CreateClient(JoinCode.Abstractions.Mcp.Client.McpServerConnectionConfig config, bool enableFallback, ILogger? logger = null) => _client;
}
