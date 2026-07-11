namespace Host.Tests.ChatCommands.Bridge;

using Core.Policy;
using Infrastructure.Time;
using JoinCode.Abstractions.Clock;
using JoinCode.Abstractions.Interfaces;
using JoinCode.Abstractions.Models.Telemetry;
using JoinCode.App.Builder;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
using Services.OAuth;

/// <summary>
/// ApplicationBuilder.BuildBridgeGuardServices DI 容器构建测试 — P0-D/P1 TDD
/// 验证独立 DI 容器能解析出 Guard 服务及其依赖
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
    /// 验证 BuildBridgeGuardServices 解析出的 TokenStorage / ConfigurationService 是同一实例（单例语义）
    /// 决策: ITokenStorage/IConfigurationService 注册为 Singleton，避免重复创建
    /// 注意: IRemotePolicyService 通过 AddHttpClient 注册，默认 Transient 生命周期（HttpClient 是轻量对象，
    ///       Handler 由 IHttpClientFactory 池化），不在此验证单例语义
    /// </summary>
    [Fact]
    public void BuildBridgeGuardServices_ShouldRegisterServicesAsSingleton()
    {
        // Arrange
        var fs = new IO.FileSystem.PhysicalFileSystem();

        // Act
        using var services = ApplicationBuilder.BuildBridgeGuardServices(fs);

        // Assert — 单例语义: 多次解析应返回同一实例
        var token1 = services.GetRequiredService<ITokenStorage>();
        var token2 = services.GetRequiredService<ITokenStorage>();
        var config1 = services.GetRequiredService<IConfigurationService>();
        var config2 = services.GetRequiredService<IConfigurationService>();

        token1.Should().BeSameAs(token2, "ITokenStorage 应为单例");
        config1.Should().BeSameAs(config2, "IConfigurationService 应为单例");
    }

    /// <summary>
    /// 验证 BuildBridgeGuardServices 注册了 IHttpClientFactory
    /// 这是 P1-3 的核心: HttpClient 通过 IHttpClientFactory 池化管理，避免 socket 耗尽
    /// 决策: 卫星项目 aot-httpclientfactory-test 已验证 IHttpClientFactory 与 NativeAOT 完全兼容
    /// </summary>
    [Fact]
    public void BuildBridgeGuardServices_ShouldResolveIHttpClientFactory()
    {
        // Arrange
        var fs = new IO.FileSystem.PhysicalFileSystem();

        // Act
        using var services = ApplicationBuilder.BuildBridgeGuardServices(fs);

        // Assert
        var factory = services.GetService<IHttpClientFactory>();
        factory.Should().NotBeNull("IHttpClientFactory 必须能从 DI 容器解析 — P1-3 HttpClient 通过 IHttpClientFactory 管理生命周期");
    }

    /// <summary>
    /// 验证 IRemotePolicyService 通过 AddHttpClient 注册后仍可正常解析
    /// 决策: AddHttpClient<TClient, TImplementation> 默认 Transient，但 HttpClient 实例轻量
    /// </summary>
    [Fact]
    public void BuildBridgeGuardServices_ShouldResolveRemotePolicyServiceViaHttpClientFactory()
    {
        // Arrange
        var fs = new IO.FileSystem.PhysicalFileSystem();

        // Act
        using var services = ApplicationBuilder.BuildBridgeGuardServices(fs);
        var policy1 = services.GetRequiredService<IRemotePolicyService>();
        var policy2 = services.GetRequiredService<IRemotePolicyService>();

        // Assert — AddHttpClient 默认 Transient: 两次解析应返回不同实例
        policy1.Should().NotBeNull("IRemotePolicyService 必须能通过 IHttpClientFactory 解析");
        policy2.Should().NotBeNull("IRemotePolicyService 必须能通过 IHttpClientFactory 解析");
        policy1.Should().NotBeSameAs(policy2, "IRemotePolicyService 通过 AddHttpClient 注册，默认 Transient 生命周期");
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

    // ─────────────────────────────────────────────────────────────────────────────
    // P1 新增测试 — RemotePolicyOptions 配置注入 + TelemetryService 接入
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 验证 BuildBridgeGuardServices 能解析出 IOptions&lt;RemotePolicyOptions&gt;
    /// 这是 P1-1 的核心: RemotePolicyService 应能从 DI 获取 options 实例
    /// 决策: 从环境变量读取配置，与 TelemetryConfig.FromEnvironment() 模式一致
    /// </summary>
    [Fact]
    public void BuildBridgeGuardServices_ShouldResolveRemotePolicyOptions()
    {
        // Arrange
        var fs = new IO.FileSystem.PhysicalFileSystem();

        // Act
        using var services = ApplicationBuilder.BuildBridgeGuardServices(fs);

        // Assert
        var options = services.GetService<IOptions<RemotePolicyOptions>>();
        options.Should().NotBeNull("IOptions<RemotePolicyOptions> 必须能从 DI 容器解析 — P1-1 接线后 RemotePolicyService 应能获取配置");
    }

    /// <summary>
    /// 验证环境变量 JCC_REMOTE_POLICY_ENDPOINT 能被读取到 options.ApiEndpoint
    /// 这是 P1-1 的核心: 用户应能通过环境变量配置远程策略服务器
    /// </summary>
    [Fact]
    public void BuildBridgeGuardServices_WhenEndpointEnvVarSet_ShouldReadIntoOptions()
    {
        // Arrange
        const string testEndpoint = "https://test-policy.example.com/api";
        var originalValue = Environment.GetEnvironmentVariable("JCC_REMOTE_POLICY_ENDPOINT");
        try
        {
            Environment.SetEnvironmentVariable("JCC_REMOTE_POLICY_ENDPOINT", testEndpoint);
            var fs = new IO.FileSystem.PhysicalFileSystem();

            // Act
            using var services = ApplicationBuilder.BuildBridgeGuardServices(fs);
            var options = services.GetRequiredService<IOptions<RemotePolicyOptions>>();

            // Assert
            options.Value.ApiEndpoint.Should().Be(testEndpoint,
                "JCC_REMOTE_POLICY_ENDPOINT 环境变量应被读取到 options.ApiEndpoint");
        }
        finally
        {
            // Cleanup — 恢复原始环境变量
            Environment.SetEnvironmentVariable("JCC_REMOTE_POLICY_ENDPOINT", originalValue);
        }
    }

    /// <summary>
    /// 验证环境变量 JCC_REMOTE_POLICY_KEY 能被读取到 options.ClientKey
    /// </summary>
    [Fact]
    public void BuildBridgeGuardServices_WhenClientKeyEnvVarSet_ShouldReadIntoOptions()
    {
        // Arrange
        const string testKey = "test-client-key-12345";
        var originalValue = Environment.GetEnvironmentVariable("JCC_REMOTE_POLICY_KEY");
        try
        {
            Environment.SetEnvironmentVariable("JCC_REMOTE_POLICY_KEY", testKey);
            var fs = new IO.FileSystem.PhysicalFileSystem();

            // Act
            using var services = ApplicationBuilder.BuildBridgeGuardServices(fs);
            var options = services.GetRequiredService<IOptions<RemotePolicyOptions>>();

            // Assert
            options.Value.ClientKey.Should().Be(testKey,
                "JCC_REMOTE_POLICY_KEY 环境变量应被读取到 options.ClientKey");
        }
        finally
        {
            Environment.SetEnvironmentVariable("JCC_REMOTE_POLICY_KEY", originalValue);
        }
    }

    /// <summary>
    /// 验证 BuildBridgeGuardServices 能解析出 ITelemetryService
    /// 这是 P1-2 的核心: RemotePolicyService 应能从 DI 获取遥测服务
    /// 决策: 使用 TelemetryService 实现（TelemetryConfig 自动从环境变量初始化）
    /// </summary>
    [Fact]
    public void BuildBridgeGuardServices_ShouldResolveTelemetryService()
    {
        // Arrange
        var fs = new IO.FileSystem.PhysicalFileSystem();

        // Act
        using var services = ApplicationBuilder.BuildBridgeGuardServices(fs);

        // Assert
        var telemetry = services.GetService<ITelemetryService>();
        telemetry.Should().NotBeNull("ITelemetryService 必须能从 DI 容器解析 — P1-2 接线后 RemotePolicyService 应能记录遥测");
    }

    /// <summary>
    /// 验证 BuildBridgeGuardServices 能解析出 TelemetryConfig
    /// 这是 P1-2 的支撑: TelemetryService 依赖 TelemetryConfig
    /// </summary>
    [Fact]
    public void BuildBridgeGuardServices_ShouldResolveTelemetryConfig()
    {
        // Arrange
        var fs = new IO.FileSystem.PhysicalFileSystem();

        // Act
        using var services = ApplicationBuilder.BuildBridgeGuardServices(fs);

        // Assert
        var config = services.GetService<TelemetryConfig>();
        config.Should().NotBeNull("TelemetryConfig 必须能从 DI 容器解析 — TelemetryService 构造函数必填依赖");
    }

    /// <summary>
    /// 验证当未设置 JCC_REMOTE_POLICY_ENDPOINT 时，options.ApiEndpoint 为空字符串
    /// 这是 fail-open 行为: 没有配置远程策略服务器时，RemotePolicyService 不刷新规则
    /// </summary>
    [Fact]
    public void BuildBridgeGuardServices_WhenNoEndpointEnvVar_ShouldDefaultToEmpty()
    {
        // Arrange
        var originalValue = Environment.GetEnvironmentVariable("JCC_REMOTE_POLICY_ENDPOINT");
        try
        {
            Environment.SetEnvironmentVariable("JCC_REMOTE_POLICY_ENDPOINT", null);
            var fs = new IO.FileSystem.PhysicalFileSystem();

            // Act
            using var services = ApplicationBuilder.BuildBridgeGuardServices(fs);
            var options = services.GetRequiredService<IOptions<RemotePolicyOptions>>();

            // Assert
            options.Value.ApiEndpoint.Should().BeEmpty(
                "未设置 JCC_REMOTE_POLICY_ENDPOINT 时，ApiEndpoint 应为空字符串（fail-open 行为）");
        }
        finally
        {
            Environment.SetEnvironmentVariable("JCC_REMOTE_POLICY_ENDPOINT", originalValue);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // P1-4 新增测试 — IClockService 注入
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 验证 BuildBridgeGuardServices 能解析出 IClockService
    /// 这是 P1-4 的核心: RemotePolicyService 应能从 DI 获取时钟服务，而非 fallback 到 SystemClockService.Instance
    /// 决策: 使用 ClockServiceFactory.Create() 支持环境变量 JCC_CLOCK_MODE 切换 Fake/Physical
    /// </summary>
    [Fact]
    public void BuildBridgeGuardServices_ShouldResolveClockService()
    {
        // Arrange
        var fs = new IO.FileSystem.PhysicalFileSystem();

        // Act
        using var services = ApplicationBuilder.BuildBridgeGuardServices(fs);

        // Assert
        var clock = services.GetService<IClockService>();
        clock.Should().NotBeNull("IClockService 必须能从 DI 容器解析 — P1-4 让 RemotePolicyService 通过 DI 获取时钟而非 fallback 到静态实例");
    }

    /// <summary>
    /// 验证 JCC_CLOCK_MODE=Fake 时，BuildBridgeGuardServices 解析出 FakeClockService
    /// 这是 P1-4 的关键能力: 支持调试和 E2E 测试时手动推进时间
    /// 决策: ClockServiceFactory.Create() 读取 JCC_CLOCK_MODE 环境变量
    /// </summary>
    [Fact]
    public void BuildBridgeGuardServices_WhenClockModeFake_ShouldResolveFakeClockService()
    {
        // Arrange
        var originalValue = Environment.GetEnvironmentVariable("JCC_CLOCK_MODE");
        try
        {
            Environment.SetEnvironmentVariable("JCC_CLOCK_MODE", "Fake");
            var fs = new IO.FileSystem.PhysicalFileSystem();

            // Act
            using var services = ApplicationBuilder.BuildBridgeGuardServices(fs);
            var clock = services.GetRequiredService<IClockService>();

            // Assert
            clock.Should().BeOfType<FakeClockService>(
                "JCC_CLOCK_MODE=Fake 时应解析为 FakeClockService — 支持手动推进时间用于调试/E2E测试");
        }
        finally
        {
            // Cleanup — 恢复原始环境变量
            Environment.SetEnvironmentVariable("JCC_CLOCK_MODE", originalValue);
        }
    }

    /// <summary>
    /// 验证未设置 JCC_CLOCK_MODE 时，BuildBridgeGuardServices 解析出 PhysicalClockService
    /// 这是 P1-4 的默认行为: 生产环境使用真实系统时间
    /// </summary>
    [Fact]
    public void BuildBridgeGuardServices_WhenClockModeUnset_ShouldResolvePhysicalClockService()
    {
        // Arrange
        var originalValue = Environment.GetEnvironmentVariable("JCC_CLOCK_MODE");
        try
        {
            Environment.SetEnvironmentVariable("JCC_CLOCK_MODE", null);
            var fs = new IO.FileSystem.PhysicalFileSystem();

            // Act
            using var services = ApplicationBuilder.BuildBridgeGuardServices(fs);
            var clock = services.GetRequiredService<IClockService>();

            // Assert
            clock.Should().BeOfType<PhysicalClockService>(
                "未设置 JCC_CLOCK_MODE 时应解析为 PhysicalClockService — 默认使用真实系统时间");
        }
        finally
        {
            // Cleanup — 恢复原始环境变量
            Environment.SetEnvironmentVariable("JCC_CLOCK_MODE", originalValue);
        }
    }
}
