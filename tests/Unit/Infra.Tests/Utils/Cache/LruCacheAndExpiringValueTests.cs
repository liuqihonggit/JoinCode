namespace Infra.Tests.Utils.Cache;

public class LruCacheTests
{
    [Fact]
    public void TryGetValue_ExistingKey_ShouldReturnValue()
    {
        var cache = new LruCache<string, int>(maxEntries: 10);
        cache.Set("a", 1);
        cache.TryGetValue("a", out var value).Should().BeTrue();
        value.Should().Be(1);
    }

    [Fact]
    public void TryGetValue_MissingKey_ShouldReturnFalse()
    {
        var cache = new LruCache<string, int>(maxEntries: 10);
        cache.TryGetValue("a", out _).Should().BeFalse();
    }

    [Fact]
    public void Set_ShouldEvictOldestWhenFull()
    {
        var cache = new LruCache<string, int>(maxEntries: 2);
        cache.Set("a", 1);
        cache.Set("b", 2);
        cache.Set("c", 3);
        cache.ContainsKey("a").Should().BeFalse();
        cache.ContainsKey("b").Should().BeTrue();
        cache.ContainsKey("c").Should().BeTrue();
    }

    [Fact]
    public void TryGetValue_ShouldPromoteKey()
    {
        var cache = new LruCache<string, int>(maxEntries: 2);
        cache.Set("a", 1);
        cache.Set("b", 2);
        cache.TryGetValue("a", out _);
        cache.Set("c", 3);
        cache.ContainsKey("a").Should().BeTrue();
        cache.ContainsKey("b").Should().BeFalse();
    }

    [Fact]
    public void Remove_ExistingKey_ShouldReturnTrue()
    {
        var cache = new LruCache<string, int>(maxEntries: 10);
        cache.Set("a", 1);
        cache.Remove("a").Should().BeTrue();
        cache.ContainsKey("a").Should().BeFalse();
    }

    [Fact]
    public void Remove_MissingKey_ShouldReturnFalse()
    {
        var cache = new LruCache<string, int>(maxEntries: 10);
        cache.Remove("a").Should().BeFalse();
    }

    [Fact]
    public void Clear_ShouldRemoveAll()
    {
        var cache = new LruCache<string, int>(maxEntries: 10);
        cache.Set("a", 1);
        cache.Set("b", 2);
        cache.Clear();
        cache.Count.Should().Be(0);
    }

    [Fact]
    public void Set_Overwrite_ShouldUpdateValue()
    {
        var cache = new LruCache<string, int>(maxEntries: 10);
        cache.Set("a", 1);
        cache.Set("a", 2);
        cache.TryGetValue("a", out var value).Should().BeTrue();
        value.Should().Be(2);
    }

    [Fact]
    public void Count_ShouldReturnCurrentCount()
    {
        var cache = new LruCache<string, int>(maxEntries: 10);
        cache.Count.Should().Be(0);
        cache.Set("a", 1);
        cache.Count.Should().Be(1);
        cache.Set("b", 2);
        cache.Count.Should().Be(2);
    }

    [Fact]
    public void SizeBytes_ShouldTrackSize()
    {
        var cache = new LruCache<string, int>(maxEntries: 10, sizeCalculator: v => v);
        cache.Set("a", 100);
        cache.CurrentSizeBytes.Should().Be(100);
        cache.Set("b", 200);
        cache.CurrentSizeBytes.Should().Be(300);
    }
}

public class ExpiringValueTests
{
    [Fact]
    public void GetOrRefresh_FirstCall_ShouldInitialize()
    {
        var callCount = 0;
        var expiring = new ExpiringValue<int>(() => { callCount++; return 42; }, TimeSpan.FromMinutes(1));
        expiring.GetOrRefresh().Should().Be(42);
        callCount.Should().Be(1);
    }

    [Fact]
    public void GetOrRefresh_WithinInterval_ShouldReturnCached()
    {
        var callCount = 0;
        var expiring = new ExpiringValue<int>(() => { callCount++; return 42; }, TimeSpan.FromMinutes(1));
        expiring.GetOrRefresh();
        expiring.GetOrRefresh();
        callCount.Should().Be(1);
    }

    [Fact]
    public void Invalidate_ShouldForceRefresh()
    {
        var callCount = 0;
        var expiring = new ExpiringValue<int>(() => { callCount++; return 42; }, TimeSpan.FromMinutes(1));
        expiring.GetOrRefresh();
        expiring.Invalidate();
        expiring.GetOrRefresh();
        callCount.Should().Be(2);
    }
}
