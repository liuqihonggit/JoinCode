namespace Core.Utils;

public sealed class BoundedObjectPool<T> where T : class
{
    private readonly ConcurrentBag<T> _pool = new();
    private readonly Func<T> _factory;
    private readonly Action<T>? _reset;
    private readonly Func<T, bool>? _returnValidator;
    private readonly int _maxPoolSize;

    public int Count => _pool.Count;

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

    public T Rent()
    {
        if (_pool.TryTake(out var item))
        {
            _reset?.Invoke(item);
            return item;
        }

        return _factory();
    }

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

    public (int Count, int MaxSize) GetStats() => (_pool.Count, _maxPoolSize);
}
