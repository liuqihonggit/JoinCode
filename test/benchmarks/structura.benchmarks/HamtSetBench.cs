namespace StructuraBenchmarks;

/// <summary>ImmutableHamTSet 微基准 — 验证 Add 双查找优化(TryAddInternal)提速。Add 新元素应快,Add 已存在持平。</summary>
[MemoryDiagnoser]
[ShortRunJob]
public class HamtSetBench {
    private ImmutableHamTSet<string> _set = null!;

    [Params(10000, 100000)]
    public int Size { get; set; }

    [GlobalSetup]
    public void Setup() {
        _set = ImmutableHamTSet<string>.Empty;
        for (var i = 0; i < Size; i++) _set = _set.Add($"key_{i}");
    }

    [Benchmark(Description = "Add 新元素(双查找优化)")]
    public ImmutableHamTSet<string> Add_New() {
        var s = _set;
        for (var i = 0; i < 1000; i++) s = s.Add($"new_{i}_{i}");
        return s;
    }

    [Benchmark(Description = "Add 已存在(对照)")]
    public ImmutableHamTSet<string> Add_Existing() {
        var s = _set;
        for (var i = 0; i < 1000; i++) s = s.Add($"key_{i}");
        return s;
    }
}
