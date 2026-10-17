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

    public T? Get<T>(string path) {
        if (!_values.TryGetValue(path, out var value)) {
            return default;
        }
        if (value is null) {
            return default;
        }
        if (value is T typed) {
            return typed;
        }
        return default;
    }
}

public sealed class ConfigRootTests {
    private static IMemoryCache NewCache() => new MemoryCache(new MemoryCacheOptions());

    [Fact]
    public void Get_LocalHasValue_ReturnsLocal() {
        var user = new StubConfigLayer();
        var pro = new StubConfigLayer();
        var local = new StubConfigLayer();
        local.Set("current.ui.theme", "dark");
        var root = new UnifiedConfigRoot(user, pro, local, NewCache());

        var result = root.Get<string>("current.ui.theme");

        Assert.Equal("dark", result);
    }

    [Fact]
    public void Get_ProHasValue_LocalNull_ReturnsPro() {
        var user = new StubConfigLayer();
        var pro = new StubConfigLayer();
        var local = new StubConfigLayer();
        pro.Set("current.ui.theme", "light");
        var root = new UnifiedConfigRoot(user, pro, local, NewCache());

        var result = root.Get<string>("current.ui.theme");

        Assert.Equal("light", result);
    }

    [Fact]
    public void Get_UserHasValue_ProLocalNull_ReturnsUser() {
        var user = new StubConfigLayer();
        var pro = new StubConfigLayer();
        var local = new StubConfigLayer();
        user.Set("current.ui.theme", "system");
        var root = new UnifiedConfigRoot(user, pro, local, NewCache());

        var result = root.Get<string>("current.ui.theme");

        Assert.Equal("system", result);
    }

    [Fact]
    public void Get_AllLayersNull_ReturnsDefault() {
        var user = new StubConfigLayer();
        var pro = new StubConfigLayer();
        var local = new StubConfigLayer();
        var root = new UnifiedConfigRoot(user, pro, local, NewCache());

        var result = root.Get<string>("current.ui.theme");

        Assert.Null(result);
    }

    [Fact]
    public void Get_WithDefaultValue_AllLayersNull_ReturnsDefaultValue() {
        var user = new StubConfigLayer();
        var pro = new StubConfigLayer();
        var local = new StubConfigLayer();
        var root = new UnifiedConfigRoot(user, pro, local, NewCache());

        var result = root.Get("current.ui.theme", "fallback");

        Assert.Equal("fallback", result);
    }

    [Fact]
    public void Get_LocalOverridesPro() {
        var user = new StubConfigLayer();
        var pro = new StubConfigLayer();
        var local = new StubConfigLayer();
        user.Set("current.ui.theme", "system");
        pro.Set("current.ui.theme", "light");
        local.Set("current.ui.theme", "dark");
        var root = new UnifiedConfigRoot(user, pro, local, NewCache());

        var result = root.Get<string>("current.ui.theme");

        Assert.Equal("dark", result);
    }

    [Fact]
    public void Get_SamePathTwice_UsesCache() {
        var user = new StubConfigLayer();
        var pro = new StubConfigLayer();
        var local = new StubConfigLayer();
        local.Set("current.ui.theme", "dark");
        var cache = NewCache();
        var root = new UnifiedConfigRoot(user, pro, local, cache);

        var result1 = root.Get<string>("current.ui.theme");
        var result2 = root.Get<string>("current.ui.theme");

        Assert.Equal("dark", result1);
        Assert.Equal("dark", result2);
    }

    [Fact]
    public void Invalidate_AfterInvalidate_ReReadsFromLayers() {
        var user = new StubConfigLayer();
        var pro = new StubConfigLayer();
        var local = new StubConfigLayer();
        local.Set("current.ui.theme", "dark");
        var root = new UnifiedConfigRoot(user, pro, local, NewCache());

        var result1 = root.Get<string>("current.ui.theme");
        local.Set("current.ui.theme", "light");
        root.Invalidate("current.ui.theme");
        var result2 = root.Get<string>("current.ui.theme");

        Assert.Equal("dark", result1);
        Assert.Equal("light", result2);
    }

    [Fact]
    public void GetSection_ReturnsSectionWithRelativeAccess() {
        var user = new StubConfigLayer();
        var pro = new StubConfigLayer();
        var local = new StubConfigLayer();
        local.Set("current.ui.theme", "dark");
        local.Set("current.ui.language", "zh");
        var root = new UnifiedConfigRoot(user, pro, local, NewCache());

        var section = root.GetSection("current.ui");
        var theme = section.Get<string>("theme");
        var language = section.Get<string>("language");

        Assert.Equal("dark", theme);
        Assert.Equal("zh", language);
    }
}
