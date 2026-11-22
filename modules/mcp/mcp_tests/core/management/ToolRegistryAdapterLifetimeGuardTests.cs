namespace Mcp.Tests.Core.Management;

/// <summary>
/// ToolRegistryAdapter 生命周期守卫测试（TASK065 断层9 根因修复）—
/// 适配器是 DI 单例的非 owner 包装：命令层用 await using 持有 IMcpToolRegistry（斜杠命令
/// /mcp /doctor /debuglog 等 8 处历史违规形态）时，adapter 的 DisposeAsync 曾转发释放底层
/// LocalToolRegistry——TUI 长驻会话中一条命令结束后注册表即死（后续 /tools 报
/// "锁 'AsyncLock#2' 已释放"，--selftest-commands 探针实证）。
/// 守卫契约：非 owner 适配器 DisposeAsync 零资源转发（底层 registry 生命周期归 DI 容器）。
/// </summary>
public sealed class ToolRegistryAdapterLifetimeGuardTests
{
    /// <summary>非 owner 契约 — adapter.DisposeAsync 不得调用底层 registry.DisposeAsync
    ///（修复前红：转发释放；修复后绿：零转发）。命令层 await using 借用单例自此无破坏力。</summary>
    [Fact]
    public async Task AdapterDispose_NeverDisposesUnderlyingRegistry() {
        var registry = new Mock<IToolRegistry>();
        registry.Setup(r => r.DisposeAsync()).Returns(ValueTask.CompletedTask);
        var remoteManager = new RemoteClientManager(registry.Object, Mock.Of<ILogger<RemoteClientManager>>());
        var adapter = new ToolRegistryAdapter(registry.Object, remoteManager);

        await adapter.DisposeAsync();

        registry.Verify(r => r.DisposeAsync(), Times.Never,
            "adapter 是非 owner 包装（LocalToolRegistry 为 DI 单例，生命周期归容器）——DisposeAsync 不得转发释放底层注册表");
    }

    /// <summary>注册转发完整对照 — RegisterToolAsync 等业务方法照常转发底层
    ///（守卫只锁释放语义，不误伤业务转发）。</summary>
    [Fact]
    public async Task AdapterRegister_ForwardsToUnderlyingRegistry() {
        var registry = new Mock<IToolRegistry>();
        registry.Setup(r => r.RegisterToolAsync(It.IsAny<IToolHandler>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var remoteManager = new RemoteClientManager(registry.Object, Mock.Of<ILogger<RemoteClientManager>>());
        var adapter = new ToolRegistryAdapter(registry.Object, remoteManager);
        var handler = new Mock<IToolHandler>();

        await adapter.RegisterToolAsync(handler.Object);
        await adapter.DisposeAsync();

        registry.Verify(r => r.RegisterToolAsync(handler.Object, It.IsAny<CancellationToken>()), Times.Once);
    }
}
