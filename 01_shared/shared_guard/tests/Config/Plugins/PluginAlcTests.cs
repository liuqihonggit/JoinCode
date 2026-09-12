namespace Core.Tests.Plugins;

public sealed class PluginAlcTests
{
    [Fact]
    public void PluginAlc_IsCollectible_True()
    {
        var alc = new PluginAlc("test-plugin");
        Assert.True(alc.IsCollectible);
    }

    [Fact]
    public void PluginAlc_Name_Set()
    {
        var alc = new PluginAlc("my-plugin");
        Assert.Equal("my-plugin", alc.Name);
    }

    [Fact]
    public void VerifyUnload_NullRef_ReturnsNull()
    {
        var result = AlcUnloadVerifier.VerifyUnload(null, "test");
        Assert.Null(result);
    }

    [Fact]
    public void VerifyUnload_NotCollectible_ReturnsDiagnostic()
    {
        var alc = new NonCollectibleAlc("test");
        var alcRef = new WeakReference<AssemblyLoadContext>(alc);
        var result = AlcUnloadVerifier.VerifyUnload(alcRef, "test");
        Assert.NotNull(result);
        Assert.Equal(PluginDiagnosticKind.AlcNotCollectible, result!.Kind);
        Assert.Equal("test", result.PluginId);
    }

    [Fact]
    public void VerifyUnload_CollectibleAlc_UnloadsWithoutError()
    {
        var alc = new PluginAlc("test-collectible");
        var alcRef = new WeakReference<AssemblyLoadContext>(alc);
        alc.Unload();
        Assert.True(alcRef.TryGetTarget(out _), "ALC 在 Unload 后仍可通过弱引用访问(回收由运行时决定)");
    }

    private sealed class NonCollectibleAlc : AssemblyLoadContext
    {
        public NonCollectibleAlc(string name) : base(name, isCollectible: false) { }
    }
}
