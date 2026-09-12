#pragma warning disable JCC3010, JCC3011, JCC3012
namespace Core.Tests.Services;

/// <summary>
/// MemoryCacheService 测试 - 使用共享静态配置（已通过分片锁保护）
/// </summary>
public class MemoryCacheServiceTests : IDisposable {
    private readonly MemoryCacheService _cacheService;

    public MemoryCacheServiceTests() {
        _cacheService = new MemoryCacheService();
        // 启用快速测试模式
        TestConfiguration.IsFastTestMode = true;
        TestConfiguration.TimeAccelerationFactor = 10;
    }

    public void Dispose() {
        // 清理快速测试模式设置
        TestConfiguration.IsFastTestMode = false;
        _cacheService.Dispose();
    }

    [Fact]
    public void SetAndGet_ShouldStoreAndRetrieveValue() {
        // Arrange
        var key = "test-key";
        var value = "test-value";

        // Act
        _cacheService.Set(key, value);
        var result = _cacheService.Get<string>(key);

        // Assert
        Assert.Equal(value, result);
    }

    [Fact]
    public void Get_NonExistentKey_ShouldReturnDefault() {
        // Arrange
        var key = "non-existent-key";

        // Act
        var result = _cacheService.Get<string>(key);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void Remove_ExistingKey_ShouldRemoveValue() {
        // Arrange
        var key = "test-key";
        var value = "test-value";
        _cacheService.Set(key, value);

        // Act
        var removed = _cacheService.Remove(key);
        var result = _cacheService.Get<string>(key);

        // Assert
        Assert.True(removed);
        Assert.Null(result);
    }

    [Fact]
    public void Remove_NonExistentKey_ShouldReturnFalse() {
        // Arrange
        var key = "non-existent-key";

        // Act
        var removed = _cacheService.Remove(key);

        // Assert
        Assert.False(removed);
    }

    [Fact]
    public void ContainsKey_ExistingKey_ShouldReturnTrue() {
        // Arrange
        var key = "test-key";
        var value = "test-value";
        _cacheService.Set(key, value);

        // Act
        var contains = _cacheService.ContainsKey(key);

        // Assert
        Assert.True(contains);
    }

    [Fact]
    public void ContainsKey_NonExistentKey_ShouldReturnFalse() {
        // Arrange
        var key = "non-existent-key";

        // Act
        var contains = _cacheService.ContainsKey(key);

        // Assert
        Assert.False(contains);
    }

    [Fact]
    public void Clear_ShouldRemoveAllValues() {
        // Arrange
        _cacheService.Set("key1", "value1");
        _cacheService.Set("key2", "value2");

        // Act
        _cacheService.Clear();

        // Assert
        Assert.False(_cacheService.ContainsKey("key1"));
        Assert.False(_cacheService.ContainsKey("key2"));
    }

    [Fact]
    public async Task SetAsync_ShouldStoreValue() {
        // Arrange
        var key = "async-key";
        var value = "async-value";

        // Act
        await _cacheService.SetAsync(key, value).ConfigureAwait(true);
        var result = await _cacheService.GetAsync<string>(key).ConfigureAwait(true);

        // Assert
        Assert.Equal(value, result);
    }

    [Fact]
    public async Task RemoveAsync_ExistingKey_ShouldRemoveValue() {
        // Arrange
        var key = "async-key";
        var value = "async-value";
        await _cacheService.SetAsync(key, value).ConfigureAwait(true);

        // Act
        var removed = await _cacheService.RemoveAsync(key).ConfigureAwait(true);

        // Assert
        Assert.True(removed);
    }

    [Fact]
    public async Task ContainsKeyAsync_ExistingKey_ShouldReturnTrue() {
        // Arrange
        var key = "async-key";
        var value = "async-value";
        await _cacheService.SetAsync(key, value).ConfigureAwait(true);

        // Act
        var contains = await _cacheService.ContainsKeyAsync(key).ConfigureAwait(true);

        // Assert
        Assert.True(contains);
    }

    [Fact]
    public async Task ClearAsync_ShouldRemoveAllValues() {
        // Arrange
        await _cacheService.SetAsync("key1", "value1").ConfigureAwait(true);
        await _cacheService.SetAsync("key2", "value2").ConfigureAwait(true);

        // Act
        await _cacheService.ClearAsync().ConfigureAwait(true);

        // Assert
        Assert.False(await _cacheService.ContainsKeyAsync("key1").ConfigureAwait(true));
        Assert.False(await _cacheService.ContainsKeyAsync("key2").ConfigureAwait(true));
    }

    [Fact]
    public void Set_WithExpiration_ShouldExpireAfterTime() {
        // Arrange
        var key = "expiring-key";
        var value = "expiring-value";
        // 使用极短过期时间，Set 后条目立即过期，无需 Task.Delay 等待
        var expiration = TimeSpan.FromTicks(1);

        // Act
        _cacheService.Set(key, value, expiration);

        // 触发过期扫描（MemoryCache 依赖系统时间，1 tick 已过期，TryGetValue 自身也会检查过期）
        _cacheService.TriggerExpirationScanForTests();

        var afterExpire = _cacheService.Get<string>(key);

        // Assert
        // Set 后立即 Get 能返回值 已由 SetAndGet_ShouldStoreAndRetrieveValue 覆盖
        // 此处验证：极短过期时间的条目在过期扫描后不可获取
        Assert.Null(afterExpire);
    }

    [Fact]
    public void Set_DifferentTypes_ShouldStoreAndRetrieve() {
        // Arrange & Act & Assert
        _cacheService.Set("int", 42);
        Assert.Equal(42, _cacheService.Get<int>("int"));

        _cacheService.Set("bool", true);
        Assert.True(_cacheService.Get<bool>("bool"));

        _cacheService.Set("double", 3.14);
        Assert.Equal(3.14, _cacheService.Get<double>("double"));

        var list = new List<string> { "a", "b", "c" };
        _cacheService.Set("list", list);
        Assert.Equal(list, _cacheService.Get<List<string>>("list"));
    }
}
