namespace Core.Tests.Plugins;

public sealed class CapabilityGateTests
{
    [Fact]
    public void RequireService_Declared_ReturnsService()
    {
        var gate = new CapabilityGate(new[] { "tools", "sessions" });
        var result = gate.RequireService<string>("tools", () => "resolved");
        Assert.Equal("resolved", result);
    }

    [Fact]
    public void RequireService_NotDeclared_ThrowsWithFixHint()
    {
        var gate = new CapabilityGate(new[] { "tools" });
        var ex = Assert.Throws<ServiceNotDeclaredException>(() =>
            gate.RequireService<string>("sessions", () => "x"));
        Assert.Equal("sessions", ex.ServiceName);
        Assert.Contains("[INF-CAPABILITY]", ex.Message);
        Assert.Contains("sessions", ex.Message);
        Assert.Contains("inject", ex.Message);
    }

    [Fact]
    public void IsDeclared_TrueForDeclared()
    {
        var gate = new CapabilityGate(new[] { "tools", "sessions" });
        Assert.True(gate.IsDeclared("tools"));
        Assert.True(gate.IsDeclared("sessions"));
    }

    [Fact]
    public void IsDeclared_FalseForNotDeclared()
    {
        var gate = new CapabilityGate(new[] { "tools" });
        Assert.False(gate.IsDeclared("sessions"));
    }

    [Fact]
    public void DeclaredServices_ReturnsAllDeclared()
    {
        var gate = new CapabilityGate(new[] { "tools", "sessions", "llm" });
        Assert.Equal(3, gate.DeclaredServices.Count);
        Assert.Contains("llm", gate.DeclaredServices);
    }

    [Fact]
    public void Exception_DeclaredInjects_StoredCorrectly()
    {
        var gate = new CapabilityGate(new[] { "a", "b" });
        var ex = Assert.Throws<ServiceNotDeclaredException>(() =>
            gate.RequireService<string>("c", () => "x"));
        Assert.Equal(new[] { "a", "b" }, ex.DeclaredInjects);
    }

    [Fact]
    public void EmptyGate_RejectsAll()
    {
        var gate = new CapabilityGate(Array.Empty<string>());
        Assert.Throws<ServiceNotDeclaredException>(() =>
            gate.RequireService<string>("any", () => "x"));
        Assert.False(gate.IsDeclared("any"));
    }

    [Fact]
    public void InjectAttribute_StoresServices()
    {
        var attr = new InjectAttribute("tools", "sessions");
        Assert.Equal(new[] { "tools", "sessions" }, attr.Services);
    }

    [Fact]
    public void InjectAttribute_EmptyServices()
    {
        var attr = new InjectAttribute();
        Assert.Empty(attr.Services);
    }

    [Fact]
    public void RequireService_NullResolve_ThrowsNre()
    {
        var gate = new CapabilityGate(new[] { "tools" });
        Assert.Throws<NullReferenceException>(() => gate.RequireService<string>("tools", null!));
    }
}
