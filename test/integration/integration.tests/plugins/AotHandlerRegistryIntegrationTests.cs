namespace Integration.Tests.Plugins;

/// <summary>
/// AOT handler 注册集成测试 — 用 <see cref="IsolatedTestHost"/> 注入隔离文件系统，
/// 不再写进程全局环境变量（xUnit 类间并行安全）。
/// </summary>
public sealed class AotHandlerRegistryIntegrationTests {
    [Fact]
    public async Task IAotHandlerRegistry_IsRegistered_InDiContainer() {
        await using var host = new IsolatedTestHost();

        var registry = host.GetService<IAotHandlerRegistry>();

        registry.Should().NotBeNull("IAotHandlerRegistry 应在 AddAotHandlerRegistry 中注册到 DI 容器");
    }

    [Fact]
    public async Task IAotHandlerRegistry_ReturnsHandlers_ForShellNode() {
        await using var host = new IsolatedTestHost();
        var registry = host.GetRequiredService<IAotHandlerRegistry>();

        var handlers = registry.GetHandlers("shell");

        handlers.Should().NotBeNull("shell 节点应有 AOT handler 注册");
        handlers.Should().NotBeEmpty("shell 节点应至少有一个工具 handler");
    }

    [Fact]
    public async Task IAotHandlerRegistry_ReturnsHandlers_ForFileNode() {
        await using var host = new IsolatedTestHost();
        var registry = host.GetRequiredService<IAotHandlerRegistry>();

        var handlers = registry.GetHandlers("file");

        handlers.Should().NotBeNull("file 节点应有 AOT handler 注册");
        handlers.Should().NotBeEmpty("file 节点应至少有一个工具 handler");
    }

    [Fact]
    public async Task IAotHandlerRegistry_ReturnsNull_ForUnknownNode() {
        await using var host = new IsolatedTestHost();
        var registry = host.GetRequiredService<IAotHandlerRegistry>();

        var handlers = registry.GetHandlers("non-existent-node-id");

        handlers.Should().BeNull("未知节点 ID 应返回 null");
    }

    [Fact]
    public async Task IAotHandlerRegistry_NodeIds_AlignWithToolCategoryEnumValue() {
        await using var host = new IsolatedTestHost();
        var registry = host.GetRequiredService<IAotHandlerRegistry>();

        var shellHandlers = registry.GetHandlers("shell");
        var fileHandlers = registry.GetHandlers("file");
        var webHandlers = registry.GetHandlers("web");

        shellHandlers.Should().NotBeNull("ToolCategory.Shell 的 EnumValue 是 'shell'");
        fileHandlers.Should().NotBeNull("ToolCategory.File 的 EnumValue 是 'file'");
        webHandlers.Should().NotBeNull("ToolCategory.Web 的 EnumValue 是 'web'");
    }
}
