namespace Core.Tests.Plugins;

public sealed class PluginDependencyGraphTests {
    private interface IService { }
    private interface IAnotherService { }

    [Fact]
    public void GetUnloadOrder_DependentsFirst_ProviderLast() {
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
    public void GetUnloadOrder_ServiceHotSwap_DynamicResolve() {
        var graph = new PluginDependencyGraph();
        graph.DeclareServiceDependency("E", typeof(IService));
        var order = graph.GetUnloadOrder("D2", t => t == typeof(IService) ? "D2" : null);
        Assert.Equal(new[] { "E", "D2" }, order);
    }

    [Fact]
    public void RemovePlugin_ClearsDeclarations() {
        var graph = new PluginDependencyGraph();
        graph.DeclareServiceDependency("B", typeof(IService));
        graph.RemovePlugin("B");
        var order = graph.GetUnloadOrder("D", t => t == typeof(IService) ? "D" : null);
        Assert.Equal(new[] { "D" }, order);
    }

    [Fact]
    public void GetUnloadOrder_NoProvider_SkipsEdge() {
        var graph = new PluginDependencyGraph();
        graph.DeclareServiceDependency("B", typeof(IService));
        var order = graph.GetUnloadOrder("D", _ => null);
        Assert.Equal(new[] { "D" }, order);
    }

    [Fact]
    public void Describe_ShowsDependencyRelations() {
        var graph = new PluginDependencyGraph();
        graph.DeclareServiceDependency("B", typeof(IService));
        var desc = graph.Describe(t => t == typeof(IService) ? "D" : null);
        Assert.Contains("D", desc);
        Assert.Contains("B", desc);
    }

    // === 补充测试:插件名级依赖 ===

    [Fact]
    public void DeclarePluginDependency_GetDependents_ReturnsDependents() {
        var graph = new PluginDependencyGraph();
        graph.DeclarePluginDependency("B", "A"); // B 依赖 A
        graph.DeclarePluginDependency("C", "A"); // C 依赖 A

        var dependents = graph.GetDependents("A");
        Assert.Equal(2, dependents.Count);
        Assert.Contains("B", dependents);
        Assert.Contains("C", dependents);
    }

    [Fact]
    public void GetDependents_NoDependents_ReturnsEmpty() {
        var graph = new PluginDependencyGraph();

        var dependents = graph.GetDependents("nonexistent");
        Assert.Empty(dependents);
    }

    [Fact]
    public void DeclarePluginDependency_DuplicateDependency_NoDoubleAdd() {
        var graph = new PluginDependencyGraph();
        graph.DeclarePluginDependency("B", "A");
        graph.DeclarePluginDependency("B", "A"); // 重复声明

        var dependents = graph.GetDependents("A");
        Assert.Single(dependents);
        Assert.Equal("B", dependents[0]);
    }

    [Fact]
    public void RemovePlugin_ClearsPluginDependencies_ReverseEdges() {
        var graph = new PluginDependencyGraph();
        graph.DeclarePluginDependency("B", "A"); // _pluginDependencies["A"] = {"B"}
        graph.DeclarePluginDependency("C", "B"); // _pluginDependencies["B"] = {"C"}

        // 移除 B:应清理 _pluginDependencies["B"] 和所有反向边(从 A 的依赖集合中移除 B)
        graph.RemovePlugin("B");

        // B 不再是 A 的依赖者
        var dependentsOfA = graph.GetDependents("A");
        Assert.DoesNotContain("B", dependentsOfA);
        // B 自身的依赖者集合已移除
        var dependentsOfB = graph.GetDependents("B");
        Assert.Empty(dependentsOfB);
    }

    // === 补充测试:边界场景 ===

    [Fact]
    public void GetUnloadOrder_EmptyGraph_ReturnsRootOnly() {
        var graph = new PluginDependencyGraph();

        var order = graph.GetUnloadOrder("solo", _ => null);
        Assert.Single(order);
        Assert.Equal("solo", order[0]);
    }

    [Fact]
    public void GetUnloadOrder_SingleNode_ReturnsRootOnly() {
        var graph = new PluginDependencyGraph();
        graph.DeclareServiceDependency("solo", typeof(IService));

        var order = graph.GetUnloadOrder("solo", _ => null);
        Assert.Single(order);
        Assert.Equal("solo", order[0]);
    }

    [Fact]
    public void GetUnloadOrder_Cycle_AToBToA_TerminatesWithoutException() {
        // 环场景:A 依赖 IService1(提供者 B),B 依赖 IService2(提供者 A)
        // Visit(A) → dependents[A]={B} → Visit(B) → dependents[B]={A} → Visit(A) 已 visited,截断
        var graph = new PluginDependencyGraph();
        graph.DeclareServiceDependency("A", typeof(IService));
        graph.DeclareServiceDependency("B", typeof(IAnotherService));

        // resolver: IService 的提供者是 B,IAnotherService 的提供者是 A
        string? resolver(Type t) => t == typeof(IService) ? "B" : t == typeof(IAnotherService) ? "A" : null;

        var order = graph.GetUnloadOrder("A", resolver);

        // 环被 visited 集合截断,不抛 StackOverflow,结果包含 A 和 B
        Assert.NotEmpty(order);
        Assert.Contains("A", order);
        Assert.Contains("B", order);
        // A 在 B 之后(B 是 A 的 dependent,先访问)
        var orderList = order.ToList();
        Assert.True(orderList.IndexOf("B") < orderList.IndexOf("A"));
    }

    [Fact]
    public void GetUnloadOrder_MultipleDependents_DependentsBeforeProvider() {
        var graph = new PluginDependencyGraph();
        graph.DeclareServiceDependency("B", typeof(IService));
        graph.DeclareServiceDependency("C", typeof(IService));
        graph.DeclareServiceDependency("D", typeof(IService));

        var order = graph.GetUnloadOrder("A", t => t == typeof(IService) ? "A" : null);

        // A 应在最后(被依赖者后卸载)
        Assert.Equal("A", order[order.Count - 1]);
        // B, C, D 都在 A 之前
        Assert.Equal(4, order.Count);
        var orderList = order.ToList();
        var idxA = orderList.IndexOf("A");
        var idxB = orderList.IndexOf("B");
        var idxC = orderList.IndexOf("C");
        var idxD = orderList.IndexOf("D");
        Assert.True(idxB < idxA);
        Assert.True(idxC < idxA);
        Assert.True(idxD < idxA);
    }

    [Fact]
    public void Describe_EmptyGraph_ReturnsEmptyMessage() {
        var graph = new PluginDependencyGraph();

        var desc = graph.Describe(_ => null);
        Assert.Contains("空", desc);
    }

    [Fact]
    public void RemovePlugin_NonExistent_NoOp() {
        var graph = new PluginDependencyGraph();

        // 不抛异常即可
        graph.RemovePlugin("nonexistent");
    }

    [Fact]
    public void DeclareServiceDependency_DuplicateType_NoDoubleAdd() {
        var graph = new PluginDependencyGraph();
        graph.DeclareServiceDependency("B", typeof(IService));
        graph.DeclareServiceDependency("B", typeof(IService)); // 重复

        var order = graph.GetUnloadOrder("A", t => t == typeof(IService) ? "A" : null);
        // B 只出现一次
        Assert.Single(order, x => x == "B");
    }
}