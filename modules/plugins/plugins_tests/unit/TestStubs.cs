#pragma warning disable JCC9108, JCC9202
namespace JoinCode.Plugins.Tests.Unit;

/// <summary>
/// 工作流插件测试桩 — 继承 WorkflowPluginBase,可配置 LoadAsync/InitializeAsync 返回值,记录调用次数
/// <para>用于 PluginManager.LoadWorkflowPluginCoreAsync 全流程确定性测试</para>
/// <para>JCC9108/JCC9202 抑制:测试 stub 故意保留可变属性供测试配置</para>
/// </summary>
internal sealed class StubWorkflowPlugin : WorkflowPluginBase {
    private readonly string _name;

    /// <summary>LoadAsync 返回值(默认 Ok)</summary>
    public OperationResult LoadResult { get; set; } = OperationResult.Ok();
    /// <summary>InitializeAsync 返回值(默认 Ok)</summary>
    public OperationResult InitResult { get; set; } = OperationResult.Ok();
    /// <summary>LoadAsync 抛异常(非 null 时优先于 LoadResult)</summary>
    public Exception? LoadException { get; set; }
    /// <summary>InitializeAsync 抛异常(非 null 时优先于 InitResult)</summary>
    public Exception? InitException { get; set; }
    /// <summary>LoadAsync 调用次数</summary>
    public int LoadCallCount { get; private set; }
    /// <summary>InitializeAsync 调用次数</summary>
    public int InitCallCount { get; private set; }
    /// <summary>是否响应 CancellationToken(ThrowIfCancellationRequested)</summary>
    public bool RespectCancellation { get; set; }
    /// <summary>收到的最后一个 CancellationToken</summary>
    public CancellationToken LastLoadCancellationToken { get; private set; }
    /// <summary>是否在 LoadAsync 中注册异步副作用(用于撤销链逆序测试)</summary>
    public bool RegisterAsyncSideEffects { get; set; }
    /// <summary>异步撤销链 Dispose 执行顺序记录</summary>
    public List<string> AsyncDisposeOrder { get; } = new();

    /// <summary>构造测试桩插件</summary>
    /// <param name="name">插件名</param>
    public StubWorkflowPlugin(string name) : base(name) => _name = name;

    /// <inheritdoc />
    public override string Name => _name;
    /// <inheritdoc />
    public override string Version => "1.0.0";
    /// <inheritdoc />
    public override string Description => "test stub";

    /// <inheritdoc />
    public override Task<OperationResult> LoadAsync(PluginContext ctx, CancellationToken cancellationToken = default) {
        LoadCallCount++;
        LastLoadCancellationToken = cancellationToken;
        if (RespectCancellation) cancellationToken.ThrowIfCancellationRequested();
        if (LoadException is not null) throw LoadException;
        if (RegisterAsyncSideEffects) {
            ctx.Effect(() => new TrackingAsyncDisposable(() => AsyncDisposeOrder.Add("first")));
            ctx.Effect(() => new TrackingAsyncDisposable(() => AsyncDisposeOrder.Add("second")));
            ctx.Effect(() => new TrackingAsyncDisposable(() => AsyncDisposeOrder.Add("third")));
        }
        return Task.FromResult(LoadResult);
    }

    /// <inheritdoc />
    public override Task<OperationResult> InitializeAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default) {
        InitCallCount++;
        if (RespectCancellation) cancellationToken.ThrowIfCancellationRequested();
        if (InitException is not null) throw InitException;
        return Task.FromResult(InitResult);
    }
}

/// <summary>
/// 带依赖声明的工作流插件测试桩 — 实现 IPluginDependencies,用于测试 DeclarePluginDependencies
/// </summary>
internal sealed class StubWorkflowPluginWithDependencies : WorkflowPluginBase, IPluginDependencies {
    private readonly string _name;
    private readonly IReadOnlyList<string> _dependencies;

    /// <summary>构造带依赖声明的测试桩插件</summary>
    /// <param name="name">插件名</param>
    /// <param name="dependencies">依赖的插件名列表</param>
    public StubWorkflowPluginWithDependencies(string name, params string[] dependencies) : base(name) {
        _name = name;
        _dependencies = dependencies;
    }

    /// <inheritdoc />
    public override string Name => _name;
    /// <inheritdoc />
    public override string Version => "1.0.0";
    /// <inheritdoc />
    public override string Description => "test stub with deps";
    /// <inheritdoc />
    public IReadOnlyList<string> Dependencies => _dependencies;

    /// <inheritdoc />
    public override Task<OperationResult> LoadAsync(PluginContext ctx, CancellationToken cancellationToken = default)
        => Task.FromResult(OperationResult.Ok());

    /// <inheritdoc />
    public override Task<OperationResult> InitializeAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
        => Task.FromResult(OperationResult.Ok());
}

/// <summary>
/// 卸载契约失败的测试桩 — ValidateUnloadContract 返回 Invalid
/// </summary>
internal sealed class StubWorkflowPluginWithBadContract : WorkflowPluginBase {
    private readonly string _name;

    /// <summary>构造契约失败测试桩</summary>
    /// <param name="name">插件名</param>
    public StubWorkflowPluginWithBadContract(string name) : base(name) => _name = name;

    /// <inheritdoc />
    public override string Name => _name;
    /// <inheritdoc />
    public override string Version => "1.0.0";
    /// <inheritdoc />
    public override string Description => "test stub with bad contract";

    /// <inheritdoc />
    public override PluginUnloadContract ValidateUnloadContract()
        => PluginUnloadContract.Invalid("测试故意违约");

    /// <inheritdoc />
    public override Task<OperationResult> LoadAsync(PluginContext ctx, CancellationToken cancellationToken = default)
        => Task.FromResult(OperationResult.Ok());

    /// <inheritdoc />
    public override Task<OperationResult> InitializeAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
        => Task.FromResult(OperationResult.Ok());
}

/// <summary>
/// 跟踪异步可释放对象 — 记录 DisposeAsync 调用,用于验证撤销链逆序执行
/// </summary>
internal sealed class TrackingAsyncDisposable : IAsyncDisposable {
    private readonly Action _onDispose;
    private int _disposed;

    /// <summary>构造跟踪异步可释放</summary>
    /// <param name="onDispose">DisposeAsync 时执行的回调</param>
    public TrackingAsyncDisposable(Action onDispose) => _onDispose = onDispose;

    /// <inheritdoc />
    public ValueTask DisposeAsync() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return ValueTask.CompletedTask;
        _onDispose();
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// 卸载时抛异常的测试桩 — OnUnload 抛异常,用于验证部分失败继续卸载
/// </summary>
internal sealed class StubWorkflowPluginThrowOnUnload : WorkflowPluginBase {
    private readonly string _name;

    /// <summary>是否在 OnUnload 时抛异常</summary>
    public bool ThrowOnUnload { get; set; }

    /// <summary>构造卸载抛异常测试桩</summary>
    /// <param name="name">插件名</param>
    public StubWorkflowPluginThrowOnUnload(string name) : base(name) => _name = name;

    /// <inheritdoc />
    public override string Name => _name;
    /// <inheritdoc />
    public override string Version => "1.0.0";
    /// <inheritdoc />
    public override string Description => "test stub throw on unload";

    /// <inheritdoc />
    protected override void OnUnload() {
        if (ThrowOnUnload) throw new InvalidOperationException("unload boom");
    }

    /// <inheritdoc />
    public override Task<OperationResult> LoadAsync(PluginContext ctx, CancellationToken cancellationToken = default)
        => Task.FromResult(OperationResult.Ok());

    /// <inheritdoc />
    public override Task<OperationResult> InitializeAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
        => Task.FromResult(OperationResult.Ok());
}
#pragma warning restore JCC9108, JCC9202
