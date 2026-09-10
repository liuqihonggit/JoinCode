namespace Core.Tests.Plugins;

public sealed class PluginDependencyGraphTests
{
    private interface IService { }

    [Fact]
    public void GetUnloadOrder_DependentsFirst_ProviderLast()
    {
        var graph = new PluginDependencyGraph();
        graph.DeclareServiceDependency("B", typeof(IService));
        graph.DeclareServiceDependency("C", typeof(IService));
        var order = graph.GetUnloadOrder("D", t => t == typeof(IService) ? "D" : null);
        Assert.Equal("D", order[order.Count - 1]);
        Assert.Contains("B", order);
        Assert.Contains("C", order);
        Assert.Equal(3, order.Count);
    }

    [Fact]
    public void GetUnloadOrder_ServiceHotSwap_DynamicResolve()
    {
        var graph = new PluginDependencyGraph();
        graph.DeclareServiceDependency("E", typeof(IService));
        var order = graph.GetUnloadOrder("D2", t => t == typeof(IService) ? "D2" : null);
        Assert.Equal(new[] { "E", "D2" }, order);
    }

    [Fact]
    public void RemovePlugin_ClearsDeclarations()
    {
        var graph = new PluginDependencyGraph();
        graph.DeclareServiceDependency("B", typeof(IService));
        graph.RemovePlugin("B");
        var order = graph.GetUnloadOrder("D", t => t == typeof(IService) ? "D" : null);
        Assert.Equal(new[] { "D" }, order);
    }

    [Fact]
    public void GetUnloadOrder_NoProvider_SkipsEdge()
    {
        var graph = new PluginDependencyGraph();
        graph.DeclareServiceDependency("B", typeof(IService));
        var order = graph.GetUnloadOrder("D", _ => null);
        Assert.Equal(new[] { "D" }, order);
    }

    [Fact]
    public void Describe_ShowsDependencyRelations()
    {
        var graph = new PluginDependencyGraph();
        graph.DeclareServiceDependency("B", typeof(IService));
        var desc = graph.Describe(t => t == typeof(IService) ? "D" : null);
        Assert.Contains("D", desc);
        Assert.Contains("B", desc);
    }
}
