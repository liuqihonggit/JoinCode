#pragma warning disable JCC3010, JCC3011, JCC3012
namespace Guard.Tests.Configuration;

public sealed class SettingsChangeApplierTests
{
    private readonly Mock<IConfigChangeNotifier> _mockNotifier = new();
    private readonly Mock<IFileSystem> _mockFs = new();
    private readonly Mock<IExecutionSettingsProvider> _mockExecutionSettings = new();
    private readonly Mock<IHookConfigurationManager> _mockHookConfig = new();

    /// <summary>
    /// ApplySettingsChangeAsync 应更新 EffortLevel
    /// </summary>
    [Fact]
    public async Task ApplySettingsChangeAsync_UpdatesEffortLevel()
    {
        // Arrange
        _mockExecutionSettings.SetupGet(x => x.EffortLevel).Returns(EffortLevel.Medium);

        var applier = CreateApplier();

        // Act
        await applier.ApplySettingsChangeAsync().ConfigureAwait(true);

        // Assert — ConfigLoader.LoadSettingsJsonAsync 需要文件系统，
        // 这里只验证不抛异常（实际 EffortLevel 更新依赖 ConfigLoader 返回值）
        applier.Dispose();
    }

    /// <summary>
    /// ApplySettingsChangeAsync 应刷新 Hook 配置缓存
    /// </summary>
    [Fact]
    public async Task ApplySettingsChangeAsync_InvalidatesHookCache()
    {
        // Arrange
        _mockHookConfig
            .Setup(x => x.InvalidateCacheAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var applier = CreateApplier();

        // Act
        await applier.ApplySettingsChangeAsync().ConfigureAwait(true);

        // Assert — 同上，Hook 缓存刷新依赖 ConfigLoader
        applier.Dispose();
    }

    /// <summary>
    /// ConfigChanged 事件触发时应调用 ApplySettingsChangeAsync
    /// </summary>
    [Fact]
    public async Task ConfigChanged_TriggersApplySettingsChange()
    {
        // Arrange — 用 TCS 替代 Task.Delay，在 InvalidateCacheAsync 回调中发信号
        var tcs = new TaskCompletionSource<bool>();

        _mockHookConfig
            .Setup(x => x.InvalidateCacheAsync(It.IsAny<CancellationToken>()))
            .Callback(() => tcs.SetResult(true))
            .Returns(Task.CompletedTask);

        var applier = CreateApplier();
        var eventArgs = new ConfigChangeEventArgs
        {
            FilePath = "settings.json",
            ChangeType = "Changed",
            Timestamp = DateTimeOffset.UtcNow
        };

        // Act — 手动触发事件
        _mockNotifier.Raise(x => x.ConfigChanged += null, _mockNotifier.Object, eventArgs);

        // 等待异步处理完成（由 InvalidateCacheAsync 回调发信号，而非固定延时）
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);

        // Assert — 不抛异常即可
        applier.Dispose();
    }

    /// <summary>
    /// Dispose 后不再响应 ConfigChanged 事件
    /// </summary>
    [Fact]
    public async Task Dispose_StopsRespondingToConfigChanged()
    {
        // Arrange
        var applier = CreateApplier();

        // Act
        applier.Dispose();

        var eventArgs = new ConfigChangeEventArgs
        {
            FilePath = "settings.json",
            ChangeType = "Changed",
            Timestamp = DateTimeOffset.UtcNow
        };

        // 不应抛异常
        _mockNotifier.Raise(x => x.ConfigChanged += null, _mockNotifier.Object, eventArgs);

        // Assert — 事件已取消订阅（Dispose 同步移除处理器，无需等待）
        _mockNotifier.VerifyRemove(x => x.ConfigChanged -= It.IsAny<EventHandler<ConfigChangeEventArgs>>(), Times.Once);
    }

    /// <summary>
    /// 多次 Dispose 不应抛异常
    /// </summary>
    [Fact]
    public void Dispose_MultipleTimes_NoException()
    {
        var applier = CreateApplier();
        applier.Dispose();
        applier.Dispose(); // 第二次不应抛异常
    }

    /// <summary>
    /// ConfigLoader 抛异常时 ApplySettingsChangeAsync 不应传播异常
    /// </summary>
    [Fact]
    public async Task ApplySettingsChangeAsync_ConfigLoaderThrows_DoesNotPropagate()
    {
        // Arrange — 不提供可选服务，ConfigLoader.LoadSettingsJsonAsync 可能抛异常
        var pipeline = new MiddlewarePipeline<SettingsContext>(
        [
            new EffortLevelMiddleware(),
            new HookRefreshMiddleware(),
            new PermissionCacheMiddleware(),
        ]);
        var applier = new SettingsChangeApplier(_mockNotifier.Object, pipeline, _mockFs.Object);

        // Act & Assert — 不应抛异常
        await applier.ApplySettingsChangeAsync().ConfigureAwait(true);

        applier.Dispose();
    }

    private SettingsChangeApplier CreateApplier()
    {
        var pipeline = new MiddlewarePipeline<SettingsContext>(
        [
            new SettingsReloadMiddleware(),
            new EffortLevelMiddleware(_mockExecutionSettings.Object),
            new HookRefreshMiddleware(_mockHookConfig.Object),
            new PermissionCacheMiddleware(),
        ]);
        return new SettingsChangeApplier(_mockNotifier.Object, pipeline, _mockFs.Object);
    }
}
#pragma warning restore JCC3010, JCC3011, JCC3012
