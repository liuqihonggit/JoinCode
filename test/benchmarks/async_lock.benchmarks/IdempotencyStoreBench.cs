namespace AsyncLockBenchmarks;

/// <summary>
/// 幂等去重存储基准测试 — 对比 IdempotencyStore(ImmutableDictionary+CAS) vs ConcurrentDictionary。
/// </summary>
[MemoryDiagnoser]
[ShortRunJob]
public class IdempotencyStoreBench {
    private IdempotencyStore _store = null!;
    private ConcurrentDictionary<IdempotencyKey, string> _concurrentDict = null!;
    private IdempotencyKey _hitKey;
    private IdempotencyKey _missKey;
    private IdempotencyKey[] _keys = null!;

    /// <summary>键数量</summary>
    [Params(100, 1000)]
    public int KeyCount { get; set; }

    [GlobalSetup]
    public void Setup() {
        _store = new IdempotencyStore();
        _concurrentDict = new ConcurrentDictionary<IdempotencyKey, string>();
        _hitKey = new IdempotencyKey("flow-hit", "op-0");
        _missKey = new IdempotencyKey("flow-miss", "op-0");
        _keys = new IdempotencyKey[KeyCount];
        for (var i = 0; i < KeyCount; i++)
            _keys[i] = new IdempotencyKey($"flow-{i}", "op-0");

        _store.TryRegister(_hitKey, "value");
        _concurrentDict.TryAdd(_hitKey, "value");
        foreach (var k in _keys) {
            _store.TryRegister(k, "value");
            _concurrentDict.TryAdd(k, "value");
        }
    }

    [Benchmark(Description = "IdempotencyStore.TryRegister 首次")]
    public bool IdempotencyStore_TryRegister_First() {
        var key = new IdempotencyKey("flow-new", "op-new");
        return _store.TryRegister(key, "value");
    }

    [Benchmark(Description = "IdempotencyStore.TryRegister 重复")]
    public bool IdempotencyStore_TryRegister_Duplicate()
        => _store.TryRegister(_hitKey, "value");

    [Benchmark(Description = "IdempotencyStore.TryGetResult 命中")]
    public bool IdempotencyStore_TryGetResult_Hit()
        => _store.TryGetResult<string>(_hitKey, out _);

    [Benchmark(Description = "IdempotencyStore.TryGetResult 未命中")]
    public bool IdempotencyStore_TryGetResult_Miss()
        => _store.TryGetResult<string>(_missKey, out _);

    [Benchmark(Description = "ConcurrentDictionary.TryGetValue 命中")]
    public bool ConcurrentDict_TryGetValue_Hit()
        => _concurrentDict.TryGetValue(_hitKey, out _);

    [Benchmark(Description = "ConcurrentDictionary.TryGetValue 未命中")]
    public bool ConcurrentDict_TryGetValue_Miss()
        => _concurrentDict.TryGetValue(_missKey, out _);

    [Benchmark(Description = "IdempotencyStore 并发注册不同键")]
    public async Task IdempotencyStore_ConcurrentRegister() {
        var store = new IdempotencyStore();
        var tasks = _keys.Select(k => Task.Run(() => store.TryRegister(k, "value")));
        await Task.WhenAll(tasks);
    }

    [Benchmark(Description = "ConcurrentDictionary 并发注册不同键")]
    public async Task ConcurrentDict_ConcurrentAdd() {
        var dict = new ConcurrentDictionary<IdempotencyKey, string>();
        var tasks = _keys.Select(k => Task.Run(() => dict.TryAdd(k, "value")));
        await Task.WhenAll(tasks);
    }
}
