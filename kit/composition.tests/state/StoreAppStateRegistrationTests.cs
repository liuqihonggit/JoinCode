namespace Sync.Tests.State;

/// <summary>
/// Store&lt;AppState&gt; DI 注册测试
/// 验证手动注册 IStore&lt;AppState&gt; 和 IStorePersistence&lt;AppState&gt; 后可正确解析
/// </summary>
public sealed class StoreAppStateRegistrationTests
{
    /// <summary>
    /// 验证手动注册 IStore&lt;AppState&gt; 后可从 DI 容器解析
    /// </summary>
    [Fact]
    public async Task ManualRegistration_StoreAppState_CanBeResolved()
    {
        // Arrange: 手动模拟 AddCoreServices 中的注册逻辑
        var services = new ServiceCollection();
        services.AddLogging();

        // 注册 Store<AppState>（不依赖 StateService，使用 null 持久化）
        services.AddSingleton<global::State.IStore<JoinCode.Abstractions.State.AppState>>(sp =>
        {
            var logger = sp.GetService<ILogger<global::State.Store<JoinCode.Abstractions.State.AppState>>>();
            var initialState = JoinCode.Abstractions.State.AppState.Default;
            return new global::State.Store<JoinCode.Abstractions.State.AppState>(initialState, null, logger);
        });

        // Act
        using var provider = services.BuildServiceProvider();
        var store = provider.GetService<global::State.IStore<JoinCode.Abstractions.State.AppState>>();

        // Assert
        store.Should().NotBeNull();
        store!.GetState().Should().NotBeNull();

        await Task.Delay(1).ConfigureAwait(true); // 满足 async 要求
    }

    /// <summary>
    /// 验证 IStorePersistence&lt;AppState&gt; 注册后可解析
    /// </summary>
    [Fact]
    public async Task ManualRegistration_StorePersistence_CanBeResolved()
    {
        // Arrange: 使用 mock IStorePersistence<AppState>
        var services = new ServiceCollection();
        services.AddLogging();

        var mockPersistence = new Mock<JoinCode.Abstractions.State.IStorePersistence<JoinCode.Abstractions.State.AppState>>();
        mockPersistence
            .Setup(p => p.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((JoinCode.Abstractions.State.AppState?)null);

        services.AddSingleton(mockPersistence.Object);

        // Act
        using var provider = services.BuildServiceProvider();
        var persistence = provider.GetService<JoinCode.Abstractions.State.IStorePersistence<JoinCode.Abstractions.State.AppState>>();

        // Assert
        persistence.Should().NotBeNull();

        await Task.Delay(1).ConfigureAwait(true); // 满足 async 要求
    }

    /// <summary>
    /// 验证 Store&lt;AppState&gt; 无持久化时仍可正常工作
    /// </summary>
    [Fact]
    public async Task StoreAppState_WithoutPersistence_WorksCorrectly()
    {
        // Arrange: 不注册 IStorePersistence，Store 应使用默认状态
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<global::State.IStore<JoinCode.Abstractions.State.AppState>>(sp =>
        {
            var logger = sp.GetService<ILogger<global::State.Store<JoinCode.Abstractions.State.AppState>>>();
            return new global::State.Store<JoinCode.Abstractions.State.AppState>(JoinCode.Abstractions.State.AppState.Default, null, logger);
        });

        // Act
        using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<global::State.IStore<JoinCode.Abstractions.State.AppState>>();

        // Assert: AppState 是 record，Default 创建新实例，Store 内部可能修改
        store.GetState().Should().NotBeNull();

        await Task.Delay(1).ConfigureAwait(true); // 满足 async 要求
    }
}
