namespace JoinCode.Composition.NodeRegistry.Tests;

/// <summary>
/// NodeServiceResolver 单元测试 — 验证 (consumerId, nodeId) 组合 key 解析行为
/// </summary>
public sealed class NodeServiceResolverTest {
    [Fact]
    public void Build_EmptyFactories_ReturnsEmptyResolver() {
        var resolver = NodeServiceResolver.Build([]);
        resolver.Should().NotBeNull();
    }

    [Fact]
    public void TryResolve_ExistingKey_ReturnsTrueAndService() {
        var resolver = NodeServiceResolver.Build([
            KVP("consumer-a:node-1", _ => "service-instance"),
        ]);

        var found = resolver.TryResolve("consumer-a", "node-1", NullServiceProvider.Instance, out var service);

        found.Should().BeTrue();
        service.Should().Be("service-instance");
    }

    [Fact]
    public void TryResolve_NonExistingKey_ReturnsFalseAndNull() {
        var resolver = NodeServiceResolver.Build([
            KVP("consumer-a:node-1", _ => "service-instance"),
        ]);

        var found = resolver.TryResolve("consumer-b", "node-1", NullServiceProvider.Instance, out var service);

        found.Should().BeFalse();
        service.Should().BeNull();
    }

    [Fact]
    public void TryResolve_SameNodeIdDifferentConsumer_ResolvesCorrectly() {
        var resolver = NodeServiceResolver.Build([
            KVP("preprocess-pipeline:crash-snapshot-mw", _ => "preprocess-instance"),
            KVP("chatinit-pipeline:crash-snapshot-mw", _ => "chatinit-instance"),
        ]);

        resolver.TryResolve("preprocess-pipeline", "crash-snapshot-mw", NullServiceProvider.Instance, out var s1);
        s1.Should().Be("preprocess-instance");

        resolver.TryResolve("chatinit-pipeline", "crash-snapshot-mw", NullServiceProvider.Instance, out var s2);
        s2.Should().Be("chatinit-instance");
    }

    [Fact]
    public void TryResolve_GenericVersion_ReturnsTypedService() {
        var resolver = NodeServiceResolver.Build([
            KVP("consumer-a:node-1", _ => "typed-service"),
        ]);

        var found = resolver.TryResolve<string>("consumer-a", "node-1", NullServiceProvider.Instance, out var service);

        found.Should().BeTrue();
        service.Should().Be("typed-service");
    }

    [Fact]
    public void TryResolve_GenericVersion_TypeMismatch_ReturnsFalse() {
        var resolver = NodeServiceResolver.Build([
            KVP("consumer-a:node-1", _ => 42),
        ]);

        var found = resolver.TryResolve<string>("consumer-a", "node-1", NullServiceProvider.Instance, out var service);

        found.Should().BeFalse();
        service.Should().BeNull();
    }

    [Fact]
    public void CombineKey_ReturnsConsumerIdColonNodeId() {
        NodeServiceResolver.CombineKey("preprocess-pipeline", "crash-snapshot-mw")
            .Should().Be("preprocess-pipeline:crash-snapshot-mw");
    }

    [Fact]
    public void TryResolve_FactoryReceivesServiceProvider() {
        var sp = new TestServiceProvider("test-value");
        var resolver = NodeServiceResolver.Build([
            KVP("consumer-a:node-1", sp => sp.GetRequiredService<string>()),
        ]);

        resolver.TryResolve("consumer-a", "node-1", sp, out var service);
        service.Should().Be("test-value");
    }

    private static KeyValuePair<string, Func<IServiceProvider, object>> KVP(string key, Func<IServiceProvider, object> factory)
        => new(key, factory);

    private sealed class NullServiceProvider : IServiceProvider {
        public static readonly NullServiceProvider Instance = new();
        public object? GetService(Type serviceType) => null;
    }

    private sealed class TestServiceProvider(string value) : IServiceProvider {
        public object? GetService(Type serviceType) => serviceType == typeof(string) ? value : null;
    }
}
