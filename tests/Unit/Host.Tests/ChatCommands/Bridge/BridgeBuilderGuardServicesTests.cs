namespace Host.Tests.ChatCommands.Bridge;

using JoinCode.Abstractions.Interfaces;
using JoinCode.App.Builder;
using Services.OAuth;

/// <summary>
/// ApplicationBuilder.BuildBridgeGuardServices DI 容器构建测试 — P0-D TDD
/// 验证独立 DI 容器能解析出 3 个 Guard 服务实例
/// 决策: 独立容器+手动注册最小服务集，避免引入完整 Host 初始化
/// </summary>
public sealed class BridgeBuilderGuardServicesTests
{
    /// <summary>
    /// 验证 BuildBridgeGuardServices 能解析出全部 3 个 Guard 服务
    /// 这是 P0-D 的核心: 接线后服务必须可解析（非 null）
    /// </summary>
    [Fact]
    public void BuildBridgeGuardServices_ShouldResolveAllThreeGuardServices()
    {
        // Arrange
        var fs = new IO.FileSystem.PhysicalFileSystem();

        // Act
        using var services = ApplicationBuilder.BuildBridgeGuardServices(fs);

        // Assert — 3 个服务必须全部解析成功
        var policyService = services.GetService<IRemotePolicyService>();
        var tokenStorage = services.GetService<ITokenStorage>();
        var configService = services.GetService<IConfigurationService>();

        policyService.Should().NotBeNull("IRemotePolicyService 必须能从 DI 容器解析 — P0-D 接线后生产环境应启用策略检查");
        tokenStorage.Should().NotBeNull("ITokenStorage 必须能从 DI 容器解析 — P0-D 接线后生产环境应启用 Token 加载");
        configService.Should().NotBeNull("IConfigurationService 必须能从 DI 容器解析 — P0-D 接线后生产环境应启用 remoteDialogSeen 读取");
    }

    /// <summary>
    /// 验证 BuildBridgeGuardServices 解析出的服务是同一实例（单例语义）
    /// 决策: 3 个服务都应注册为 Singleton，避免重复创建
    /// </summary>
    [Fact]
    public void BuildBridgeGuardServices_ShouldRegisterServicesAsSingleton()
    {
        // Arrange
        var fs = new IO.FileSystem.PhysicalFileSystem();

        // Act
        using var services = ApplicationBuilder.BuildBridgeGuardServices(fs);

        // Assert — 单例语义: 多次解析应返回同一实例
        var policy1 = services.GetRequiredService<IRemotePolicyService>();
        var policy2 = services.GetRequiredService<IRemotePolicyService>();
        var token1 = services.GetRequiredService<ITokenStorage>();
        var token2 = services.GetRequiredService<ITokenStorage>();
        var config1 = services.GetRequiredService<IConfigurationService>();
        var config2 = services.GetRequiredService<IConfigurationService>();

        policy1.Should().BeSameAs(policy2, "IRemotePolicyService 应为单例");
        token1.Should().BeSameAs(token2, "ITokenStorage 应为单例");
        config1.Should().BeSameAs(config2, "IConfigurationService 应为单例");
    }

    /// <summary>
    /// 验证 BuildBridgeGuardServices 能解析出 ILogger&lt;BridgeMainCommand&gt;
    /// 这是 BridgeMainCommand 的可选依赖，但接线后应该可用
    /// </summary>
    [Fact]
    public void BuildBridgeGuardServices_ShouldResolveLoggerForBridgeMainCommand()
    {
        // Arrange
        var fs = new IO.FileSystem.PhysicalFileSystem();

        // Act
        using var services = ApplicationBuilder.BuildBridgeGuardServices(fs);

        // Assert
        var logger = services.GetService<ILogger<JoinCode.ChatCommands.Bridge.BridgeMainCommand>>();
        logger.Should().NotBeNull("ILogger<BridgeMainCommand> 必须能从 DI 容器解析");
    }

    /// <summary>
    /// 验证 BuildBridgeGuardServices 注入的 IFileSystem 是同一个实例
    /// 决策: 确保传入的 IFileSystem 被复用，而不是新创建一个
    /// </summary>
    [Fact]
    public void BuildBridgeGuardServices_ShouldReuseInjectedFileSystem()
    {
        // Arrange
        var fs = new IO.FileSystem.PhysicalFileSystem();

        // Act
        using var services = ApplicationBuilder.BuildBridgeGuardServices(fs);
        var resolvedFs = services.GetRequiredService<IFileSystem>();

        // Assert
        resolvedFs.Should().BeSameAs(fs, "传入的 IFileSystem 实例应被 DI 容器复用");
    }

    /// <summary>
    /// 验证 BuildBridgeGuardServices 返回的 ServiceProvider 实现了 IDisposable
    /// 确保调用方可以使用 using 语句管理 DI 容器生命周期
    /// </summary>
    [Fact]
    public void BuildBridgeGuardServices_ShouldReturnDisposableServiceProvider()
    {
        // Arrange
        var fs = new IO.FileSystem.PhysicalFileSystem();

        // Act
        var services = ApplicationBuilder.BuildBridgeGuardServices(fs);

        // Assert
        services.Should().BeAssignableTo<IDisposable>("ServiceProvider 必须实现 IDisposable 以支持 using 语句");

        // Cleanup
        services.Dispose();
    }
}
