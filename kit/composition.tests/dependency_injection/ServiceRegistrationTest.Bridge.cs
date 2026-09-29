namespace Core.Tests.DependencyInjection;

/// <summary>
/// ServiceRegistration.AddBridgeServices 拆分后的 internal 子方法确定性测试。
/// <para>验证每个子方法注册了正确的 ServiceDescriptor（ServiceType + Lifetime），无 IO/无异步/无时序依赖。</para>
/// <para>用 extern alias composition（在 GlobalUsings.cs 声明）消解 ServiceRegistration 跨程序集同名冲突。</para>
/// </summary>
public sealed partial class ServiceRegistrationTest {
    private static ServiceCollection CreateServices() => new();

    /// <summary>
    /// 断言 services 中存在指定类型 + 生命周期的 ServiceDescriptor。
    /// </summary>
    private static void ShouldHaveSingleton<TService>(IServiceCollection services) {
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(TService));
        descriptor.Should().NotBeNull($"应注册 {typeof(TService).Name}");
        descriptor!.Lifetime.Should().Be(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Singleton, $"{typeof(TService).Name} 应为 Singleton");
    }

    [Fact]
    public void RegisterBridgeConfigExtraction_RegistersAllThreeConfigs() {
        var services = CreateServices();
        CompositionServiceRegistration.RegisterBridgeConfigExtraction(services);
        ShouldHaveSingleton<BridgeConfig>(services);
        ShouldHaveSingleton<SubAgentConcurrencyOptions>(services);
        ShouldHaveSingleton<SubAgentLivenessOptions>(services);
    }

    [Fact]
    public void RegisterBridgeConfigExtraction_DoesNotRegisterOtherBridgeServices() {
        var services = CreateServices();
        CompositionServiceRegistration.RegisterBridgeConfigExtraction(services);
        services.Any(d => d.ServiceType == typeof(BridgeApiClient)).Should().BeFalse("不应注册 BridgeApiClient");
        services.Any(d => d.ServiceType == typeof(TransportConfiguration)).Should().BeFalse("不应注册 TransportConfiguration");
    }

    [Fact]
    public void RegisterSubAgentStallDefense_RegistersHostedService() {
        var services = CreateServices();
        CompositionServiceRegistration.RegisterSubAgentStallDefense(services);
        services.Any(d => d.ServiceType == typeof(IHostedService)
            && d.ImplementationType == typeof(SubAgentStallDefenseHostedService)).Should().BeTrue("应注册 SubAgentStallDefenseHostedService");
    }

    [Fact]
    public void RegisterBridgeApiClientFactory_RegistersBridgeApiClientSingleton() {
        var services = CreateServices();
        CompositionServiceRegistration.RegisterBridgeApiClientFactory(services);
        ShouldHaveSingleton<BridgeApiClient>(services);
    }

    [Fact]
    public void RegisterBridgeApiClientFactory_UsesFactoryNotImplementationType() {
        var services = CreateServices();
        CompositionServiceRegistration.RegisterBridgeApiClientFactory(services);
        var descriptor = services.First(d => d.ServiceType == typeof(BridgeApiClient));
        descriptor.ImplementationFactory.Should().NotBeNull("应使用工厂注册（避免构造函数歧义）");
        descriptor.ImplementationType.Should().BeNull("不应使用 ImplementationType 注册");
    }

    [Fact]
    public void RegisterTransportConfiguration_RegistersTransportConfigurationSingleton() {
        var services = CreateServices();
        CompositionServiceRegistration.RegisterTransportConfiguration(services);
        ShouldHaveSingleton<TransportConfiguration>(services);
    }

    [Fact]
    public void RegisterTransportConfiguration_UsesFactory() {
        var services = CreateServices();
        CompositionServiceRegistration.RegisterTransportConfiguration(services);
        var descriptor = services.First(d => d.ServiceType == typeof(TransportConfiguration));
        descriptor.ImplementationFactory.Should().NotBeNull("应使用工厂注册（从 BridgeConfig 提取）");
    }

    [Fact]
    public void RegisterV1BridgeInitPipeline_RegistersPipelineSingleton() {
        var services = CreateServices();
        CompositionServiceRegistration.RegisterV1BridgeInitPipeline(services);
        ShouldHaveSingleton<MiddlewarePipeline<V1BridgeInitContext>>(services);
    }

    [Fact]
    public void RegisterV2BridgeInitPipeline_RegistersPipelineSingleton() {
        var services = CreateServices();
        CompositionServiceRegistration.RegisterV2BridgeInitPipeline(services);
        ShouldHaveSingleton<MiddlewarePipeline<V2BridgeInitContext>>(services);
    }

    [Fact]
    public void RegisterHandleWorkPipeline_RegistersPipelineSingleton() {
        var services = CreateServices();
        CompositionServiceRegistration.RegisterHandleWorkPipeline(services);
        ShouldHaveSingleton<MiddlewarePipeline<HandleWorkContext>>(services);
    }

    [Fact]
    public void RegisterShutdownPipeline_RegistersPipelineSingleton() {
        var services = CreateServices();
        CompositionServiceRegistration.RegisterShutdownPipeline(services);
        ShouldHaveSingleton<MiddlewarePipeline<ShutdownContext>>(services);
    }

    [Fact]
    public void RegisterRunPipeline_RegistersPipelineSingleton() {
        var services = CreateServices();
        CompositionServiceRegistration.RegisterRunPipeline(services);
        ShouldHaveSingleton<MiddlewarePipeline<BridgeRunContext>>(services);
    }

    [Fact]
    public void AddBridgeServices_OrchestratesAllNineSubMethods() {
        var services = CreateServices();
        CompositionServiceRegistration.AddBridgeServices(services);
        ShouldHaveSingleton<BridgeConfig>(services);
        ShouldHaveSingleton<SubAgentConcurrencyOptions>(services);
        ShouldHaveSingleton<SubAgentLivenessOptions>(services);
        ShouldHaveSingleton<BridgeApiClient>(services);
        ShouldHaveSingleton<TransportConfiguration>(services);
        ShouldHaveSingleton<MiddlewarePipeline<V1BridgeInitContext>>(services);
        ShouldHaveSingleton<MiddlewarePipeline<V2BridgeInitContext>>(services);
        ShouldHaveSingleton<MiddlewarePipeline<HandleWorkContext>>(services);
        ShouldHaveSingleton<MiddlewarePipeline<ShutdownContext>>(services);
        ShouldHaveSingleton<MiddlewarePipeline<BridgeRunContext>>(services);
    }
}
