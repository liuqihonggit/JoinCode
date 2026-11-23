namespace Core.Tests.DependencyInjection;

public sealed partial class ServiceRegistrationTest {
    /// <summary>
    /// 验证 Cache 摘边后，Composition 的生成式入口仍为每个缓存契约注册一个单例。
    /// </summary>
    [Theory]
    [InlineData(typeof(IGitStatusCache))]
    [InlineData(typeof(IImageStore))]
    [InlineData(typeof(IPasteStore))]
    [InlineData(typeof(IRepositoryCache))]
    [InlineData(typeof(ISessionEnvVars))]
    [InlineData(typeof(IFileStateCache))]
    public void AutoRegisteredServices_RegisterCacheContractAsSingleton(Type contract) {
        var services = CreateServices();
        CompositionServiceRegistration.AddJoinCodeCompositionAutoRegisteredServices(services);

        var descriptor = Assert.Single(services, service => service.ServiceType == contract);
        descriptor.Lifetime.Should().Be(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Singleton);
    }
}
