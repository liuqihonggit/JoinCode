namespace Core.Utils;

/// <summary>
/// 有界对象池 — 复用对象减少 GC 压力，支持租借、归还、容量上限和归还校验
/// </summary>
/// <typeparam name="T">池化对象类型，必须为引用类型</typeparam>
public sealed class BoundedObjectPool<T> where T : class
{
    private readonly ConcurrentBag<T> _pool = new();
    private readonly Func<T> _factory;
    private readonly Action<T>? _reset;
    private readonly Func<T, bool>? _returnValidator;
    private readonly int _maxPoolSize;

    /// <summary>
    /// 获取池中当前可用对象数量
    /// </summary>
    public int Count => _pool.Count;

    /// <summary>
    /// 构造函数 — 注入工厂、容量上限、重置回调和归还校验回调
    /// </summary>
    /// <param name="factory">新建对象的工厂函数</param>
    /// <param name="maxPoolSize">池容量上限，默认 32</param>
    /// <param name="reset">归还/租借时重置对象的回调，可为 null</param>
    /// <param name="returnValidator">归还前校验回调，返回 false 则丢弃对象，可为 null</param>
    public BoundedObjectPool(
        Func<T> factory,
        int maxPoolSize = 32,
        Action<T>? reset = null,
        Func<T, bool>? returnValidator = null)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPoolSize);
        _factory = factory;
        _maxPoolSize = maxPoolSize;
        _reset = reset;
        _returnValidator = returnValidator;
    }

    /// <summary>
    /// 从池中租借一个对象，池空时调用工厂新建
    /// </summary>
    /// <returns>租借到的对象</returns>
    public T Rent()
    {
        if (_pool.TryTake(out var item))
        {
            _reset?.Invoke(item);
            return item;
        }

        return _factory();
    }

    /// <summary>
    /// 归还对象到池中，超过容量上限或未通过校验则丢弃
    /// </summary>
    /// <param name="item">要归还的对象</param>
    public void Return(T item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (_returnValidator is not null && !_returnValidator(item))
            return;

        if (_pool.Count >= _maxPoolSize)
            return;

        _reset?.Invoke(item);
        _pool.Add(item);
    }

    /// <summary>
    /// 获取池统计信息 — 当前数量和容量上限
    /// </summary>
    /// <returns>(当前对象数量, 容量上限)</returns>
    public (int Count, int MaxSize) GetStats() => (_pool.Count, _maxPoolSize);
}
