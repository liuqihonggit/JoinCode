namespace Core.Tests.Plugins;

public sealed class PluginFiberStateTests
{
    [Fact]
    public void Fiber_InitialState_IsPending()
    {
        var fiber = new PluginFiber();
        Assert.Equal(PluginFiberState.Pending, fiber.State);
    }

    [Fact]
    public void TransitionTo_LegalChain_Succeeds()
    {
        var fiber = new PluginFiber();
        fiber.TransitionTo(PluginFiberState.Activating);
        fiber.TransitionTo(PluginFiberState.Active);
        fiber.TransitionTo(PluginFiberState.Unloading);
        fiber.TransitionTo(PluginFiberState.Unloaded);
        Assert.Equal(PluginFiberState.Unloaded, fiber.State);
    }

    [Fact]
    public void TransitionTo_IllegalTransition_Throws()
    {
        var fiber = new PluginFiber();
        fiber.TransitionTo(PluginFiberState.Activating);
        fiber.TransitionTo(PluginFiberState.Active);
        var ex = Assert.Throws<InvalidOperationException>(() => fiber.TransitionTo(PluginFiberState.Pending));
        Assert.Contains("[INF-FIBER-ILLEGAL]", ex.Message);
    }

    [Fact]
    public void TryTransitionTo_IllegalTransition_ReturnsFalse()
    {
        var fiber = new PluginFiber();
        fiber.TransitionTo(PluginFiberState.Activating);
        Assert.False(fiber.TryTransitionTo(PluginFiberState.Unloaded));
        Assert.Equal(PluginFiberState.Activating, fiber.State);
    }

    [Fact]
    public void TransitionTo_ActivatingToFailedToUnloadingToUnloaded_Succeeds()
    {
        var fiber = new PluginFiber();
        fiber.TransitionTo(PluginFiberState.Activating);
        fiber.TransitionTo(PluginFiberState.Failed);
        fiber.TransitionTo(PluginFiberState.Unloading);
        fiber.TransitionTo(PluginFiberState.Unloaded);
        Assert.Equal(PluginFiberState.Unloaded, fiber.State);
    }

    [Fact]
    public void WorkflowPluginBase_Fiber_InitialStatePending()
    {
        var plugin = new FiberTestPlugin();
        Assert.Equal(PluginFiberState.Pending, plugin.Fiber.State);
        plugin.Dispose();
    }

    [Fact]
    public void WorkflowPluginBase_UnloadTwice_SecondReturnsAlreadyUnloaded()
    {
        var plugin = new FiberTestPlugin();
        var r1 = plugin.Unload();
        Assert.True(r1.IsSuccess);
        Assert.Equal(PluginFiberState.Unloaded, plugin.Fiber.State);
        var r2 = plugin.Unload();
        Assert.Equal(PluginUnloadStatus.AlreadyUnloaded, r2.Status);
    }

    [Fact]
    public void TransitionTo_FailedToActivating_RetrySucceeds()
    {
        var fiber = new PluginFiber();
        fiber.TransitionTo(PluginFiberState.Activating);
        fiber.TransitionTo(PluginFiberState.Failed);
        fiber.TransitionTo(PluginFiberState.Activating);
        fiber.TransitionTo(PluginFiberState.Active);
        Assert.Equal(PluginFiberState.Active, fiber.State);
    }

    [Fact]
    public void TransitionTo_ActiveToFailed_Succeeds()
    {
        var fiber = new PluginFiber();
        fiber.TransitionTo(PluginFiberState.Activating);
        fiber.TransitionTo(PluginFiberState.Active);
        fiber.TransitionTo(PluginFiberState.Failed);
        Assert.Equal(PluginFiberState.Failed, fiber.State);
    }

    [Fact]
    public void TransitionTo_UnloadingToFailed_Succeeds()
    {
        var fiber = new PluginFiber();
        fiber.TransitionTo(PluginFiberState.Activating);
        fiber.TransitionTo(PluginFiberState.Active);
        fiber.TransitionTo(PluginFiberState.Unloading);
        fiber.TransitionTo(PluginFiberState.Failed);
        Assert.Equal(PluginFiberState.Failed, fiber.State);
    }

    [Fact]
    public void TransitionTo_FailedToUnloadingToUnloaded_Succeeds()
    {
        var fiber = new PluginFiber();
        fiber.TransitionTo(PluginFiberState.Activating);
        fiber.TransitionTo(PluginFiberState.Failed);
        fiber.TransitionTo(PluginFiberState.Unloading);
        fiber.TransitionTo(PluginFiberState.Unloaded);
        Assert.Equal(PluginFiberState.Unloaded, fiber.State);
    }

    private sealed class FiberTestPlugin : WorkflowPluginBase
    {
        public FiberTestPlugin() : base("fiber-test") { }
        public override string Name => "fiber-test";
        public override string Version => "1.0.0";
        public override string Description => "Fiber测试";
        public override Task<OperationResult> LoadAsync(PluginContext ctx, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Ok());
        public override Task<OperationResult> InitializeAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Ok());
    }
}
