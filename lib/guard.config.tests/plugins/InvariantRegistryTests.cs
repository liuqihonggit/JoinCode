namespace Core.Tests.Plugins;

public sealed class InvariantRegistryTests
{
    [Fact]
    public void Register_EnabledPackage_RunsInstaller()
    {
        var registry = new InvariantRegistry();
        var ran = false;
        var disposer = registry.Register("my-package", _ => ran = true);
        Assert.True(ran);
        Assert.Contains("my-package", registry.RegisteredPackages);
        disposer.Dispose();
    }

    [Fact]
    public void Register_Disabled_SkipsInstaller()
    {
        var registry = new InvariantRegistry(new InvariantRegistryOptions { Enabled = false });
        var ran = false;
        var disposer = registry.Register("my-package", _ => ran = true);
        Assert.False(ran);
        Assert.Contains("my-package", registry.RegisteredPackages);
        disposer.Dispose();
    }

    [Fact]
    public void Register_AllowlistMatch_RunsInstaller()
    {
        var registry = new InvariantRegistry(new InvariantRegistryOptions
        {
            PackageAllowlist = new[] { "^my-" },
        });
        var ran = false;
        registry.Register("my-package", _ => ran = true);
        Assert.True(ran);
    }

    [Fact]
    public void Register_AllowlistNoMatch_SkipsInstaller()
    {
        var registry = new InvariantRegistry(new InvariantRegistryOptions
        {
            PackageAllowlist = new[] { "^my-" },
        });
        var ran = false;
        registry.Register("other-package", _ => ran = true);
        Assert.False(ran);
    }

    [Fact]
    public void Register_BlocklistMatch_SkipsInstaller()
    {
        var registry = new InvariantRegistry(new InvariantRegistryOptions
        {
            PackageBlocklist = new[] { "^blocked" },
        });
        var ran = false;
        registry.Register("blocked-pkg", _ => ran = true);
        Assert.False(ran);
    }

    [Fact]
    public void Register_BlocklistPriorityOverAllowlist()
    {
        var registry = new InvariantRegistry(new InvariantRegistryOptions
        {
            PackageAllowlist = new[] { "^pkg-" },
            PackageBlocklist = new[] { "-skip$" },
        });
        var ran = false;
        registry.Register("pkg-skip", _ => ran = true);
        Assert.False(ran);
    }

    [Fact]
    public void Fail_ThrowsInvariantError_WithPackageName()
    {
        var registry = new InvariantRegistry();
        var ex = Assert.Throws<InvariantError>(() =>
            registry.Register("bad-pkg", fail => fail("trace invariant broken")));
        Assert.Equal("bad-pkg", ex.PackageName);
        Assert.Contains("trace invariant broken", ex.Message);
        Assert.Contains("bad-pkg", ex.Message);
    }

    [Fact]
    public void InvariantError_HasStableCode()
    {
        var err = new InvariantError("pkg", "msg");
        Assert.Equal("INVARIANT", InvariantError.Code);
        Assert.Equal("pkg", err.PackageName);
    }

    [Fact]
    public void Register_InstallerThrowsInvariantError_RemovesRegistration()
    {
        var registry = new InvariantRegistry();
        Assert.Throws<InvariantError>(() =>
            registry.Register("bad-pkg", fail => fail("broken")));
        Assert.DoesNotContain("bad-pkg", registry.RegisteredPackages);
    }

    [Fact]
    public void Register_DuplicatePackage_Overwrites()
    {
        var registry = new InvariantRegistry();
        var d1 = registry.Register("pkg", _ => { });
        d1.Dispose();
        var d2 = registry.Register("pkg", _ => { });
        Assert.Contains("pkg", registry.RegisteredPackages);
        d2.Dispose();
        Assert.DoesNotContain("pkg", registry.RegisteredPackages);
    }

    [Fact]
    public void Disposer_RemovesRegistration()
    {
        var registry = new InvariantRegistry();
        var disposer = registry.Register("pkg", _ => { });
        Assert.Contains("pkg", registry.RegisteredPackages);
        disposer.Dispose();
        Assert.DoesNotContain("pkg", registry.RegisteredPackages);
    }

    [Fact]
    public void Disposer_CalledTwice_IsIdempotent()
    {
        var registry = new InvariantRegistry();
        var disposer = registry.Register("pkg", _ => { });
        disposer.Dispose();
        disposer.Dispose();
        Assert.DoesNotContain("pkg", registry.RegisteredPackages);
    }

    [Fact]
    public void IsSelected_Disabled_ReturnsFalse()
    {
        var registry = new InvariantRegistry(new InvariantRegistryOptions { Enabled = false });
        Assert.False(registry.IsSelected("any"));
    }

    [Fact]
    public void IsSelected_EnabledNoFilters_ReturnsTrue()
    {
        var registry = new InvariantRegistry();
        Assert.True(registry.IsSelected("any"));
    }

    [Fact]
    public void IsSelected_AllowlistMatch_ReturnsTrue()
    {
        var registry = new InvariantRegistry(new InvariantRegistryOptions
        {
            PackageAllowlist = new[] { "^app-" },
        });
        Assert.True(registry.IsSelected("app-core"));
        Assert.False(registry.IsSelected("lib-core"));
    }

    [Fact]
    public void IsSelected_BlocklistMatch_ReturnsFalse()
    {
        var registry = new InvariantRegistry(new InvariantRegistryOptions
        {
            PackageBlocklist = new[] { "-test$" },
        });
        Assert.False(registry.IsSelected("pkg-test"));
        Assert.True(registry.IsSelected("pkg-core"));
    }

    [Fact]
    public void Register_EmptyInstaller_Succeeds()
    {
        var registry = new InvariantRegistry();
        var disposer = registry.Register("empty-pkg", _ => { });
        Assert.Contains("empty-pkg", registry.RegisteredPackages);
        disposer.Dispose();
    }

    [Fact]
    public void Register_NullPackageName_Throws()
    {
        var registry = new InvariantRegistry();
        Assert.Throws<ArgumentNullException>(() => registry.Register(null!, _ => { }));
    }

    [Fact]
    public void Register_NullInstaller_Throws()
    {
        var registry = new InvariantRegistry();
        Assert.Throws<ArgumentNullException>(() => registry.Register("pkg", null!));
    }
}
