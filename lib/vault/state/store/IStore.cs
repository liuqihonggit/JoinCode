
namespace State;

/// <summary>
/// 状态变更事件参数
/// </summary>
public sealed class StateChangedEventArgs<TState> where TState : notnull
{
    public TState OldState { get; }
    public TState NewState { get; }

    public StateChangedEventArgs(TState oldState, TState newState)
    {
        OldState = oldState;
        NewState = newState;
    }
}

/// <summary>
/// 状态订阅者接口
/// </summary>
public interface IStateSubscriber<TState> where TState : notnull
{
    void OnStateChanged(StateChangedEventArgs<TState> args);
}

/// <summary>
/// 状态变更监听器委托
/// </summary>
public delegate void StateChangedHandler<TState>(StateChangedEventArgs<TState> args) where TState : notnull;

/// <summary>
/// 响应式状态存储接口
/// 提供不可变状态的获取、更新和订阅功能
/// </summary>
public interface IStore<TState> where TState : notnull
{
    /// <summary>
    /// 获取当前状态（不可变）
    /// </summary>
    TState GetState();

    /// <summary>
    /// 异步获取当前状态
    /// </summary>
    Task<TState> GetStateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 更新状态
    /// </summary>
    /// <param name="updater">状态更新函数，接收旧状态返回新状态</param>
    void SetState(Func<TState, TState> updater);

    /// <summary>
    /// 异步更新状态
    /// </summary>
    /// <param name="updater">异步状态更新函数</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task SetStateAsync(Func<TState, Task<TState>> updater, CancellationToken cancellationToken = default);

    /// <summary>
    /// 订阅状态变更
    /// </summary>
    /// <param name="handler">状态变更处理器</param>
    /// <returns>取消订阅的 Disposable</returns>
    IDisposable Subscribe(StateChangedHandler<TState> handler);

    /// <summary>
    /// 订阅状态变更（使用接口）
    /// </summary>
    /// <param name="subscriber">状态订阅者</param>
    /// <returns>取消订阅的 Disposable</returns>
    IDisposable Subscribe(IStateSubscriber<TState> subscriber);
}

/// <summary>
/// 派生状态选择器接口
/// 用于从 Store 中选择派生状态，仅在派生值变化时通知
/// </summary>
public interface IStoreSelector<TState, TSelected> where TState : notnull
{
    /// <summary>
    /// 选择器函数
    /// </summary>
    Func<TState, TSelected> Selector { get; }

    /// <summary>
    /// 当前选中的派生值
    /// </summary>
    TSelected CurrentValue { get; }

    /// <summary>
    /// 订阅派生值变更
    /// </summary>
    /// <param name="handler">值变更处理器</param>
    /// <returns>取消订阅的 Disposable</returns>
    IDisposable Subscribe(Action<TSelected> handler);
}

/// <summary>
/// Store 扩展方法
/// </summary>
public static class StoreExtensions
{
    /// <summary>
    /// 创建派生状态选择器
    /// </summary>
    /// <typeparam name="TState">状态类型</typeparam>
    /// <typeparam name="TSelected">选中值类型</typeparam>
    /// <param name="store">Store 实例</param>
    /// <param name="selector">选择器函数</param>
    /// <returns>派生状态选择器</returns>
    public static IStoreSelector<TState, TSelected> Select<TState, TSelected>(
        this IStore<TState> store,
        Func<TState, TSelected> selector) where TState : notnull
    {
        return new StoreSelector<TState, TSelected>(store, selector);
    }

    /// <summary>
    /// 使用记录比较的选择器（用于复杂对象）
    /// </summary>
    public static IStoreSelector<TState, TSelected> SelectByValue<TState, TSelected>(
        this IStore<TState> store,
        Func<TState, TSelected> selector,
        IEqualityComparer<TSelected>? comparer = null) where TState : notnull
    {
        return new StoreSelector<TState, TSelected>(store, selector, comparer);
    }
}
