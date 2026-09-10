namespace Core.Tests.Plugins;

public sealed class ServiceInterceptRegistryTests
{
    private sealed class TestConfig
    {
        public string Name { get; set; } = "";
        public int Timeout { get; set; }
        public bool Enabled { get; set; } = true;
    }

    [Fact]
    public void ResolveConfig_NoIntercept_ReturnsBase()
    {
        var registry = new ServiceInterceptRegistry();
        var config = new TestConfig { Name = "base", Timeout = 100 };
        var result = registry.ResolveConfig("svc", config);
        Assert.Equal("base", result.Name);
        Assert.Equal(100, result.Timeout);
    }

    [Fact]
    public void Intercept_AppliesToConfig()
    {
        var registry = new ServiceInterceptRegistry();
        registry.Intercept("svc", c =>
        {
            var cfg = (TestConfig)c;
            cfg.Timeout = 500;
        });
        var result = registry.ResolveConfig("svc", new TestConfig { Name = "base", Timeout = 100 });
        Assert.Equal(500, result.Timeout);
        Assert.Equal("base", result.Name);
    }

    [Fact]
    public void MultipleIntercepts_AppliedInOrder()
    {
        var registry = new ServiceInterceptRegistry();
        var order = new List<int>();
        registry.Intercept("svc", c => { order.Add(1); ((TestConfig)c).Name = "first"; });
        registry.Intercept("svc", c => { order.Add(2); ((TestConfig)c).Name = "second"; });
        registry.Intercept("svc", c => { order.Add(3); ((TestConfig)c).Timeout = 999; });
        var result = registry.ResolveConfig("svc", new TestConfig { Name = "base", Timeout = 0 });
        Assert.Equal(new[] { 1, 2, 3 }, order);
        Assert.Equal("second", result.Name);
        Assert.Equal(999, result.Timeout);
    }

    [Fact]
    public void Disposer_RemovesIntercept()
    {
        var registry = new ServiceInterceptRegistry();
        var disposer = registry.Intercept("svc", c => ((TestConfig)c).Timeout = 500);
        Assert.True(registry.HasIntercept("svc"));
        disposer.Dispose();
        Assert.False(registry.HasIntercept("svc"));
        var result = registry.ResolveConfig("svc", new TestConfig { Timeout = 100 });
        Assert.Equal(100, result.Timeout);
    }

    [Fact]
    public void Disposer_CalledTwice_IsIdempotent()
    {
        var registry = new ServiceInterceptRegistry();
        var disposer = registry.Intercept("svc", _ => { });
        disposer.Dispose();
        disposer.Dispose();
        Assert.False(registry.HasIntercept("svc"));
    }

    [Fact]
    public void HasIntercept_TrueAfterRegister()
    {
        var registry = new ServiceInterceptRegistry();
        registry.Intercept("svc", _ => { });
        Assert.True(registry.HasIntercept("svc"));
        Assert.False(registry.HasIntercept("other"));
    }

    [Fact]
    public void InterceptCount_MultipleRegistrations()
    {
        var registry = new ServiceInterceptRegistry();
        var d1 = registry.Intercept("svc", _ => { });
        var d2 = registry.Intercept("svc", _ => { });
        Assert.Equal(2, registry.InterceptCount("svc"));
        d1.Dispose();
        Assert.Equal(1, registry.InterceptCount("svc"));
        d2.Dispose();
        Assert.Equal(0, registry.InterceptCount("svc"));
    }

    [Fact]
    public void Clear_RemovesAllIntercepts()
    {
        var registry = new ServiceInterceptRegistry();
        registry.Intercept("svc", _ => { });
        registry.Intercept("svc", _ => { });
        registry.Clear("svc");
        Assert.False(registry.HasIntercept("svc"));
    }

    [Fact]
    public void Intercept_NullServiceName_Throws()
    {
        var registry = new ServiceInterceptRegistry();
        Assert.Throws<ArgumentNullException>(() => registry.Intercept(null!, _ => { }));
    }

    [Fact]
    public void Intercept_NullOverride_Throws()
    {
        var registry = new ServiceInterceptRegistry();
        Assert.Throws<ArgumentNullException>(() => registry.Intercept("svc", null!));
    }

    [Fact]
    public void ResolveConfig_NullBase_Throws()
    {
        var registry = new ServiceInterceptRegistry();
        Assert.Throws<ArgumentNullException>(() => registry.ResolveConfig<TestConfig>("svc", null!));
    }

    [Fact]
    public void DifferentServices_IndependentIntercepts()
    {
        var registry = new ServiceInterceptRegistry();
        registry.Intercept("svc-a", c => ((TestConfig)c).Name = "A");
        registry.Intercept("svc-b", c => ((TestConfig)c).Name = "B");
        var a = registry.ResolveConfig("svc-a", new TestConfig());
        var b = registry.ResolveConfig("svc-b", new TestConfig());
        Assert.Equal("A", a.Name);
        Assert.Equal("B", b.Name);
    }
}
