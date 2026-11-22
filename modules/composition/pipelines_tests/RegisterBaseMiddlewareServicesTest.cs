namespace Pipelines.Tests;

/// <summary>
/// RegisterBaseMiddlewareServices 单元测试 — 验证基础中间件服务注册到 DI 容器
/// </summary>
public sealed class RegisterBaseMiddlewareServicesTest {
    [Fact]
    public void RegisterBaseMiddlewareServices_ReturnsServiceCollection() {
        var services = new ServiceCollection();

        var result = services.RegisterBaseMiddlewareServices();

        result.Should().BeSameAs(services);
    }

    [Fact]
    public async Task RegisterBaseMiddlewareServices_RegistersChatMiddlewareTypes() {
        var services = new ServiceCollection();
        services.AddLogging();

        services.RegisterBaseMiddlewareServices();

        await using var sp = services.BuildServiceProvider();
        sp.GetService<ChatTimingMiddleware>().Should().NotBeNull();
    }
}
