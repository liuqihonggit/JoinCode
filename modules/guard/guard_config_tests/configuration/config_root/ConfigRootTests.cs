namespace Guard.Config.Tests.Configuration.ConfigRoot;

/// <summary>
/// Stub IConfigLayer 用于 ConfigRoot 测试 — 用字典模拟单层配置。
/// </summary>
internal sealed class StubConfigLayer : IConfigLayer {
    private readonly Dictionary<string, string?> _values = new(StringComparer.Ordinal);
    public bool Exists { get; set; } = true;

    internal void Set(string path, string? value) {
        _values[path] = value;
    }

    public bool TryGet<T>(string path, [NotNullWhen(true)] out T? value) {
        if (!_values.TryGetValue(path, out var raw) || raw is null) {
            value = default;
            return false;
        }
        if (raw is T typed) {
            value = typed;
            return true;
        }
        value = default;
        return false;
    }
}

public sealed class ConfigRootTests {
    private static IMemoryCache NewCache() => new MemoryCache(new MemoryCacheOptions());
    private static StubConfigLayer E() => new();

    [Fact]
    public void TryGet_LocalHasValue_ReturnsTrueAndLocal() {
        var local = new StubConfigLayer();
        local.Set("current.ui.theme", "dark");
        var root = new UnifiedConfigRoot(E(), E(), local, NewCache());

        var found = root.TryGet<string>("current.ui.theme", out var result);

        Assert.True(found);
        Assert.Equal("dark", result);
    }

    [Fact]
    public void TryGet_ProHasValue_LocalNull_ReturnsTrueAndPro() {
        var pro = new StubConfigLayer();
        pro.Set("current.ui.theme", "light");
        var root = new UnifiedConfigRoot(E(), pro, E(), NewCache());

        var found = root.TryGet<string>("current.ui.theme", out var result);

        Assert.True(found);
        Assert.Equal("light", result);
    }

    [Fact]
    public void TryGet_UserHasValue_ProLocalNull_ReturnsTrueAndUser() {
        var user = new StubConfigLayer();
        user.Set("current.ui.theme", "system");
        var root = new UnifiedConfigRoot(user, E(), E(), NewCache());

        var found = root.TryGet<string>("current.ui.theme", out var result);

        Assert.True(found);
        Assert.Equal("system", result);
    }

    [Fact]
    public void TryGet_AllLayersNull_ReturnsFalse() {
        var root = new UnifiedConfigRoot(E(), E(), E(), NewCache());

        var found = root.TryGet<string>("current.ui.theme", out var result);

        Assert.False(found);
        Assert.Null(result);
    }

    [Fact]
    public void Get_WithDefaultValue_AllLayersNull_ReturnsDefaultValue() {
        var root = new UnifiedConfigRoot(E(), E(), E(), NewCache());

        var result = root.Get("current.ui.theme", "fallback");

        Assert.Equal("fallback", result);
    }

    [Fact]
    public void TryGet_LocalOverridesPro() {
        var user = new StubConfigLayer();
        var pro = new StubConfigLayer();
        var local = new StubConfigLayer();
        user.Set("current.ui.theme", "system");
        pro.Set("current.ui.theme", "light");
        local.Set("current.ui.theme", "dark");
        var root = new UnifiedConfigRoot(user, pro, local, NewCache());

        var found = root.TryGet<string>("current.ui.theme", out var result);

        Assert.True(found);
        Assert.Equal("dark", result);
    }

    [Fact]
    public void TryGet_SamePathTwice_UsesCache() {
        var local = new StubConfigLayer();
        local.Set("current.ui.theme", "dark");
        var root = new UnifiedConfigRoot(E(), E(), local, NewCache());

        var found1 = root.TryGet<string>("current.ui.theme", out var result1);
        var found2 = root.TryGet<string>("current.ui.theme", out var result2);

        Assert.True(found1 && found2);
        Assert.Equal("dark", result1);
        Assert.Equal("dark", result2);
    }

    [Fact]
    public void Invalidate_AfterInvalidate_ReReadsFromLayers() {
        var local = new StubConfigLayer();
        local.Set("current.ui.theme", "dark");
        var root = new UnifiedConfigRoot(E(), E(), local, NewCache());

        var found1 = root.TryGet<string>("current.ui.theme", out var result1);
        local.Set("current.ui.theme", "light");
        root.Invalidate("current.ui.theme");
        var found2 = root.TryGet<string>("current.ui.theme", out var result2);

        Assert.True(found1 && found2);
        Assert.Equal("dark", result1);
        Assert.Equal("light", result2);
    }

    [Fact]
    public void TryGetSection_ReturnsSectionWithRelativeAccess() {
        var local = new StubConfigLayer();
        local.Set("current.ui.theme", "dark");
        local.Set("current.ui.language", "zh");
        var root = new UnifiedConfigRoot(E(), E(), local, NewCache());

        var section = root.GetSection("current.ui");
        var themeFound = section.TryGet<string>("theme", out var theme);
        var langFound = section.TryGet<string>("language", out var language);

        Assert.True(themeFound && langFound);
        Assert.Equal("dark", theme);
        Assert.Equal("zh", language);
    }
}
