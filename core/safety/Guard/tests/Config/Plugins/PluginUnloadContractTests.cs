namespace Core.Tests.Plugins;

public sealed class PluginUnloadContractTests
{
    [Fact]
    public void PluginUnloadContract_Valid_HasNoViolations()
    {
        var contract = PluginUnloadContract.Valid;
        Assert.True(contract.IsValid);
        Assert.Null(contract.Reason);
        Assert.Empty(contract.Violations);
    }

    [Fact]
    public void PluginUnloadContract_Invalid_JoinsViolationsAsReason()
    {
        var contract = PluginUnloadContract.Invalid("资源A无disposer", "资源B无disposer");
        Assert.False(contract.IsValid);
        Assert.Equal("资源A无disposer; 资源B无disposer", contract.Reason);
        Assert.Equal(2, contract.Violations.Count);
    }

    [Fact]
    public void NonEmptyUndo_ConstructWithNull_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new NonEmptyUndo(null!));
    }

    [Fact]
    public void NonEmptyUndo_Invoke_ExecutesUnderlyingAction()
    {
        var invoked = false;
        var undo = new NonEmptyUndo(() => invoked = true);
        undo.Invoke();
        Assert.True(invoked);
    }

    [Fact]
    public void NonEmptyUndo_ImplicitConversionToAction_Works()
    {
        var invoked = false;
        NonEmptyUndo undo = new(() => invoked = true);
        Action action = undo;
        action();
        Assert.True(invoked);
    }

    [Fact]
    public async Task LoadWorkflowPlugin_ContractViolation_ThrowsAndRejectsLoad()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.AddSingleton<IFileSystem, PhysicalFileSystem>();
        services.AddSingleton<IPluginManager, PluginManager>();
        var sp = services.BuildServiceProvider();
        var pm = sp.GetRequiredService<IPluginManager>();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await pm.LoadWorkflowPluginAsync<ContractViolatingPlugin>().ConfigureAwait(true)).ConfigureAwait(true);

        Assert.Contains("[INF-PLUGIN-CONTRACT]", ex.Message);
        Assert.Contains("测试违规", ex.Message);
        Assert.False(pm.IsPluginLoaded("contract-violating"));
    }

    [Fact]
    public async Task LoadWorkflowPlugin_ContractValid_LoadsSuccessfully()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.AddSingleton<IFileSystem, PhysicalFileSystem>();
        services.AddSingleton<IPluginManager, PluginManager>();
        var sp = services.BuildServiceProvider();
        var pm = sp.GetRequiredService<IPluginManager>();

        var host = await pm.LoadWorkflowPluginAsync<ContractValidPlugin>().ConfigureAwait(true);

        Assert.True(pm.IsPluginLoaded("contract-valid"));
        await pm.UnloadPluginAsync("contract-valid").ConfigureAwait(true);
    }

    private sealed class ContractViolatingPlugin : WorkflowPluginBase
    {
        public ContractViolatingPlugin() : base("contract-violating") { }
        public override string Name => "contract-violating";
        public override string Version => "1.0.0";
        public override string Description => "测试用-契约违反";
        public override OperationResult Load(IServiceCollection services) => OperationResult.Ok();
        public override Task<OperationResult> InitializeAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Ok());
        public override PluginUnloadContract ValidateUnloadContract() => PluginUnloadContract.Invalid("测试违规");
    }

    private sealed class ContractValidPlugin : WorkflowPluginBase
    {
        public ContractValidPlugin() : base("contract-valid") { }
        public override string Name => "contract-valid";
        public override string Version => "1.0.0";
        public override string Description => "测试用-契约通过";
        public override OperationResult Load(IServiceCollection services) => OperationResult.Ok();
        public override Task<OperationResult> InitializeAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Ok());
    }
}
