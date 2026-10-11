namespace Infrastructure.Utils.Cpu;

/// <summary>
/// AOT 兼容的链式并行工具 — 替代 PLINQ AsParallel()，底层 Task.Run + Task.WhenAll
/// </summary>
/// <remarks>
/// PLINQ (System.Linq.Parallel) 在 NativeAOT 下产生 IL3053 警告（内部反射+动态泛型实例化），
/// 本工具用 Task.Run + Task.WhenAll 实现相同链式 API，完全 AOT 兼容。
/// </remarks>
public static class ParallelLinq {

    /// <summary>
    /// 将序列转为并行查询（AOT 兼容，替代 PLINQ AsParallel()）
    /// </summary>
    public static ParallelQuery<T> AsCpuParallel<T>(this IEnumerable<T> source) => new(source);
}

/// <summary>
/// AOT 兼容的并行查询构建器 — 链式 API 贴近 PLINQ，底层 Task.Run + Task.WhenAll
/// </summary>
public sealed class ParallelQuery<T> : IReadOnlyList<T> {

    private readonly IReadOnlyList<T> _items;
    private int _degree = Environment.ProcessorCount;
    private CancellationToken _ct;

    internal ParallelQuery(IEnumerable<T> source) {
        _items = source as IReadOnlyList<T> ?? source.ToList();
    }

    private ParallelQuery(IReadOnlyList<T> items, int degree, CancellationToken ct) {
        _items = items;
        _degree = degree;
        _ct = ct;
    }

    /// <summary>设置并行度（&lt;=0 则使用处理器核数）</summary>
    public ParallelQuery<T> WithDegreeOfParallelism(int degree) {
        _degree = degree <= 0 ? Environment.ProcessorCount : degree;
        return this;
    }

    /// <summary>设置取消令牌</summary>
    public ParallelQuery<T> WithCancellation(CancellationToken ct) {
        _ct = ct;
        return this;
    }

    /// <summary>保持顺序（本实现天然保序，此方法为 API 兼容）</summary>
    public ParallelQuery<T> AsOrdered() => this;

    /// <summary>并行映射，返回新的并行查询（保序）</summary>
    public ParallelQuery<TResult> Select<TResult>(Func<T, TResult> selector) {
        var results = new TResult[_items.Count];
        Execute((item, i) => results[i] = selector(item));
        return new ParallelQuery<TResult>(results, _degree, _ct);
    }

    /// <summary>并行过滤，返回新的并行查询（保序）</summary>
    public ParallelQuery<T> Where(Func<T, bool> predicate) {
        var flags = new bool[_items.Count];
        Execute((item, i) => flags[i] = predicate(item));
        var kept = new List<T>(_items.Count);
        for (var i = 0; i < _items.Count; i++)
            if (flags[i])
                kept.Add(_items[i]);
        return new ParallelQuery<T>(kept, _degree, _ct);
    }

    /// <summary>并行执行无返回值操作</summary>
    public void ForAll(Action<T> action) => Execute((item, _) => action(item));

    /// <summary>并行执行带取消令牌的无返回值操作</summary>
    public void ForAll(Action<T, CancellationToken> action) => Execute((item, _) => action(item, _ct));

    /// <summary>收集为数组（保序）</summary>
    public T[] ToArray() => [.. _items];

    /// <summary>收集为列表（保序）</summary>
    public List<T> ToList() => [.. _items];

    /// <summary>IReadOnlyList 实现 — 允许标准 LINQ 方法（OrderBy 等）链式调用</summary>
    public int Count => _items.Count;

    /// <summary>IReadOnlyList 实现</summary>
    public T this[int index] => _items[index];

    /// <summary>IReadOnlyList 实现</summary>
    public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _items.GetEnumerator();

    private void Execute(Action<T, int> work) {
        var count = _items.Count;
        if (count == 0)
            return;

        var degree = Math.Min(_degree, count);
        if (degree <= 1) {
            for (var i = 0; i < count; i++) {
                _ct.ThrowIfCancellationRequested();
                work(_items[i], i);
            }
            return;
        }

        var index = 0;
        var tasks = new Task[degree];
        for (var t = 0; t < degree; t++) {
            tasks[t] = Task.Run(() => {
                int i;
                while ((i = Interlocked.Increment(ref index) - 1) < count) {
                    _ct.ThrowIfCancellationRequested();
                    work(_items[i], i);
                }
            });
        }
        Task.WhenAll(tasks).GetAwaiter().GetResult();
    }
}
