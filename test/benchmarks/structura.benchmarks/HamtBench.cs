namespace StructuraBenchmarks;

/// <summary>HAMT 微基准 — 针对三个优化点精确压测: SetItem 同值(优化A 激活 ReferenceEquals 短路)、Enumerate(优化B PushChildren 提取)、Remove(退化修复)。</summary>
[MemoryDiagnoser]
[ShortRunJob]
public class HamtBench {
    private ImmutableHamT<string, int> _hamt = null!;

    [Params(10000, 100000)]
    public int Size { get; set; }

    [GlobalSetup]
    public void Setup() {
        _hamt = ImmutableHamT<string, int>.Empty;
        for (var i = 0; i < Size; i++) _hamt = _hamt.Add($"key_{i}", i);
    }

    [Benchmark(Description = "SetItem 同值(优化A)")]
    public ImmutableHamT<string, int> SetItem_SameValue() {
        var d = _hamt;
        for (var i = 0; i < 1000; i++) d = d.SetItem($"key_{i}", i);
        return d;
    }

    [Benchmark(Description = "SetItem 异值(对照)")]
    public ImmutableHamT<string, int> SetItem_DiffValue() {
        var d = _hamt;
        for (var i = 0; i < 1000; i++) d = d.SetItem($"key_{i}", i + 1000000);
        return d;
    }

    [Benchmark(Description = "Remove")]
    public ImmutableHamT<string, int> Remove() {
        var d = _hamt;
        for (var i = 0; i < 1000; i++) d = d.Remove($"key_{i}");
        return d;
    }

    [Benchmark(Description = "Enumerate(优化B)")]
    public int Enumerate() {
        var s = 0;
        foreach (var kv in _hamt) s += kv.Value;
        return s;
    }

    [Benchmark(Description = "EnumerateKeys(优化B)")]
    public int EnumerateKeys() {
        var s = 0;
        foreach (var k in _hamt.Keys) s += k.Length;
        return s;
    }
}
