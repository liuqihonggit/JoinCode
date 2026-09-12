namespace Core.Tests.Plugins;

public sealed class DynamicPluginRegistryTests
{
    private static DynamicPluginRegistry CreateRegistry(PluginApprovalRegistry? approval = null)
    {
        return new DynamicPluginRegistry(
            approval,
            clock: null,
            loader: (_, _) => FrozenDictionary<string, Func<object?[], object?>>.Empty);
    }

    private static PluginPackage DefineSample(DynamicPluginRegistry reg, string name = "alpha")
    {
        return reg.DefinePlugin(
            name,
            assemblyPath: "/tmp/fake.dll",
            version: new Version(1, 0, 0),
            entryType: "MyPlugin.Entry",
            entryMethod: "Activate",
            capabilities: FrozenSet.Create("shell", "fs"));
    }

    [Fact]
    public void DefinePlugin_RegistersPackage()
    {
        var reg = CreateRegistry();
        var pkg = DefineSample(reg);
        Assert.Equal("alpha", pkg.Name);
        Assert.Equal(new Version(1, 0, 0), pkg.Version);
        Assert.Equal(DynamicPluginState.Defined, reg.GetState("alpha"));
    }

    [Fact]
    public void DefinePlugin_DuplicateName_Throws()
    {
        var reg = CreateRegistry();
        DefineSample(reg);
        var ex = Assert.Throws<InvalidOperationException>(() => DefineSample(reg));
        Assert.Contains("DYN-DEFINE-DUP", ex.Message);
    }

    [Fact]
    public void RunPlugin_TransitionsToRunning()
    {
        var reg = CreateRegistry();
        DefineSample(reg);
        var pkgId = reg.RunPlugin("alpha");
        Assert.Equal(DynamicPluginState.Running, reg.GetState("alpha"));
        Assert.Equal(pkgId, reg.GetRunningPackageId("alpha"));
    }

    [Fact]
    public void RunPlugin_IdempotentWhenAlreadyRunning()
    {
        var reg = CreateRegistry();
        DefineSample(reg);
        var first = reg.RunPlugin("alpha");
        var second = reg.RunPlugin("alpha");
        Assert.Equal(first, second);
    }

    [Fact]
    public void RunPlugin_Undefined_Throws()
    {
        var reg = CreateRegistry();
        var ex = Assert.Throws<KeyNotFoundException>(() => reg.RunPlugin("ghost"));
        Assert.Contains("DYN-RUN-UNDEFINED", ex.Message);
    }

    [Fact]
    public void RunPlugin_PendingApproval_Throws()
    {
        var approval = new PluginApprovalRegistry();
        var reg = CreateRegistry(approval);
        DefineSample(reg);
        approval.ArmRequest("alpha", "需要审批");
        var ex = Assert.Throws<InvalidOperationException>(() => reg.RunPlugin("alpha"));
        Assert.Contains("DYN-RUN-PENDING-APPROVAL", ex.Message);
    }

    [Fact]
    public void RunPlugin_AfterApproval_Succeeds()
    {
        var approval = new PluginApprovalRegistry();
        var reg = CreateRegistry(approval);
        DefineSample(reg);
        var req = approval.ArmRequest("alpha", "需要审批");
        approval.Approve(req.RequestId);
        reg.RunPlugin("alpha");
        Assert.Equal(DynamicPluginState.Running, reg.GetState("alpha"));
    }

    [Fact]
    public void StopPlugin_TransitionsToStopped()
    {
        var reg = CreateRegistry();
        DefineSample(reg);
        reg.RunPlugin("alpha");
        Assert.True(reg.StopPlugin("alpha"));
        Assert.Equal(DynamicPluginState.Stopped, reg.GetState("alpha"));
    }

    [Fact]
    public void StopPlugin_NotRunning_ReturnsFalse()
    {
        var reg = CreateRegistry();
        DefineSample(reg);
        Assert.False(reg.StopPlugin("alpha"));
    }

    [Fact]
    public void StopPlugin_CanRerun()
    {
        var reg = CreateRegistry();
        DefineSample(reg);
        reg.RunPlugin("alpha");
        reg.StopPlugin("alpha");
        reg.RunPlugin("alpha");
        Assert.Equal(DynamicPluginState.Running, reg.GetState("alpha"));
    }

    [Fact]
    public void UndefinePlugin_RemovesDefinition()
    {
        var reg = CreateRegistry();
        DefineSample(reg);
        Assert.True(reg.UndefinePlugin("alpha"));
        Assert.Equal(DynamicPluginState.Undefined, reg.GetState("alpha"));
        Assert.Null(reg.InspectPlugin("alpha"));
    }

    [Fact]
    public void UndefinePlugin_Running_StopsAndRemoves()
    {
        var reg = CreateRegistry();
        DefineSample(reg);
        reg.RunPlugin("alpha");
        Assert.True(reg.UndefinePlugin("alpha"));
        Assert.Equal(DynamicPluginState.Undefined, reg.GetState("alpha"));
    }

    [Fact]
    public void UndefinePlugin_NotDefined_ReturnsFalse()
    {
        var reg = CreateRegistry();
        Assert.False(reg.UndefinePlugin("ghost"));
    }

    [Fact]
    public void UpdatePlugin_ReplacesVersion()
    {
        var reg = CreateRegistry();
        DefineSample(reg);
        var newPkg = reg.UpdatePlugin("alpha", "/tmp/fake2.dll", new Version(2, 0, 0), "MyPlugin.Entry", "Activate");
        Assert.Equal(new Version(2, 0, 0), newPkg.Version);
        var inspected = reg.InspectPlugin("alpha");
        Assert.Equal(new Version(2, 0, 0), inspected!.Version);
    }

    [Fact]
    public void UpdatePlugin_OlderVersion_Throws()
    {
        var reg = CreateRegistry();
        DefineSample(reg);
        var ex = Assert.Throws<ArgumentException>(() =>
            reg.UpdatePlugin("alpha", "/tmp/fake2.dll", new Version(0, 9, 0), "MyPlugin.Entry", "Activate"));
        Assert.Contains("DYN-UPDATE-VERSION", ex.Message);
    }

    [Fact]
    public void UpdatePlugin_Running_StopsOldInstance()
    {
        var reg = CreateRegistry();
        DefineSample(reg);
        reg.RunPlugin("alpha");
        reg.UpdatePlugin("alpha", "/tmp/fake2.dll", new Version(2, 0, 0), "MyPlugin.Entry", "Activate");
        Assert.Equal(DynamicPluginState.Stopped, reg.GetState("alpha"));
    }

    [Fact]
    public void InspectPlugin_ReturnsPackage()
    {
        var reg = CreateRegistry();
        var pkg = DefineSample(reg);
        var inspected = reg.InspectPlugin("alpha");
        Assert.NotNull(inspected);
        Assert.Equal(pkg.PackageId, inspected!.PackageId);
        Assert.Contains("shell", (IEnumerable<string>)inspected!.Capabilities);
    }

    [Fact]
    public void InspectPlugin_Undefined_ReturnsNull()
    {
        var reg = CreateRegistry();
        Assert.Null(reg.InspectPlugin("ghost"));
    }

    [Fact]
    public void ListPlugins_ReturnsAllDefined()
    {
        var reg = CreateRegistry();
        DefineSample(reg, "alpha");
        DefineSample(reg, "beta");
        DefineSample(reg, "gamma");
        var list = reg.ListPlugins();
        Assert.Equal(3, list.Count);
        Assert.Contains("alpha", list);
        Assert.Contains("beta", list);
        Assert.Contains("gamma", list);
    }

    [Fact]
    public void Invoke_NotRunning_ReturnsPluginNotRunning()
    {
        var reg = CreateRegistry();
        DefineSample(reg);
        var result = reg.Invoke("alpha", "foo", []);
        Assert.False(result.IsSuccess);
        Assert.Equal(PluginInvokeFailure.PluginNotRunning, result.Failure);
    }

    [Fact]
    public void Invoke_MethodNotFound()
    {
        var reg = CreateRegistry();
        DefineSample(reg);
        reg.RunPlugin("alpha");
        var result = reg.Invoke("alpha", "nonexistent", []);
        Assert.False(result.IsSuccess);
        Assert.Equal(PluginInvokeFailure.MethodNotFound, result.Failure);
    }

    [Fact]
    public void Invoke_StalePackageId_ReturnsStaleRun()
    {
        var reg = CreateRegistry();
        DefineSample(reg);
        reg.RunPlugin("alpha");
        var staleId = new PluginPackageId(999);
        var result = reg.Invoke("alpha", "foo", [], staleId);
        Assert.False(result.IsSuccess);
        Assert.Equal(PluginInvokeFailure.StaleRun, result.Failure);
    }

    [Fact]
    public void NextPackageId_Increments()
    {
        var reg = CreateRegistry();
        var id1 = reg.NextPackageId();
        var id2 = reg.NextPackageId();
        Assert.Equal(id1.Value + 1, id2.Value);
    }

    [Fact]
    public void GetRunningPackageId_NotRunning_ReturnsNull()
    {
        var reg = CreateRegistry();
        DefineSample(reg);
        Assert.Null(reg.GetRunningPackageId("alpha"));
    }

    [Fact]
    public void PluginPackageId_Equality()
    {
        var a = new PluginPackageId(42);
        var b = new PluginPackageId(42);
        var c = new PluginPackageId(43);
        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
        Assert.Equal("pkg-42", a.ToString());
    }
}
