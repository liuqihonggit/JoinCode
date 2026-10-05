namespace StructuraBenchmarks;

/// <summary>SwissTable vs BCL FrozenDictionary 压测 — 可变哈希表 vs 冻结哈希表。
/// 语义差异:SwissTable 可变(构建后仍可 Add/Remove),FrozenDictionary 构建后不可变(只读)。
/// Add 对比的是构建阶段:Swiss 逐个 Add vs (Dictionary 逐个 Add + ToFrozenDictionary() 冻结)。
/// 读取对比公平:两者都支持高效只读查找。覆盖 构建/查找命中/查找未命中/ContainsKey/枚举,str+int key。</summary>
[MemoryDiagnoser]
[ShortRunJob]
public class SwissTableVsFrozenBench {
    private SwissTable<string, int> _swissStr = null!;
    private FrozenDictionary<string, int> _frozenStr = null!;
    private string[] _strKeys = null!;
    private string[] _strLookup = null!;
    private string[] _strMissing = null!;

    private SwissTable<int, int> _swissInt = null!;
    private FrozenDictionary<int, int> _frozenInt = null!;
    private int[] _intLookup = null!;
    private int[] _intMissing = null!;

    [Params(10000, 100000)]
    public int Size { get; set; }

    [GlobalSetup]
    public void Setup() {
        _strKeys = new string[Size];
        for (var i = 0; i < Size; i++) _strKeys[i] = $"key_{i}";
        var rnd = new Random(42);
        _strLookup = new string[200000];
        for (var i = 0; i < 200000; i++) _strLookup[i] = _strKeys[rnd.Next(Size)];
        _strMissing = new string[10000];
        for (var i = 0; i < 10000; i++) _strMissing[i] = $"missing_{i}";

        _swissStr = new SwissTable<string, int>(Size);
        var tmpStr = new Dictionary<string, int>(Size);
        foreach (var k in _strKeys) { _swissStr.Add(k, k.Length); tmpStr.Add(k, k.Length); }
        _frozenStr = tmpStr.ToFrozenDictionary();

        _intLookup = new int[200000];
        for (var i = 0; i < 200000; i++) _intLookup[i] = rnd.Next(Size);
        _intMissing = new int[10000];
        for (var i = 0; i < 10000; i++) _intMissing[i] = Size + i;

        _swissInt = new SwissTable<int, int>(Size);
        var tmpInt = new Dictionary<int, int>(Size);
        for (var i = 0; i < Size; i++) { _swissInt.Add(i, i * 2); tmpInt.Add(i, i * 2); }
        _frozenInt = tmpInt.ToFrozenDictionary();
    }

    // === 构建 (Add) — string key ===
    [Benchmark(Description = "Swiss 构建 str")]
    public int Swiss_Build_Str() {
        var d = new SwissTable<string, int>(Size);
        for (var i = 0; i < Size; i++) d.Add(_strKeys[i], i);
        return d.Count;
    }

    [Benchmark(Description = "Frozen 构建 str")]
    public int Frozen_Build_Str() {
        var d = new Dictionary<string, int>(Size);
        for (var i = 0; i < Size; i++) d.Add(_strKeys[i], i);
        return d.ToFrozenDictionary().Count;
    }

    // === 构建 (Add) — int key ===
    [Benchmark(Description = "Swiss 构建 int")]
    public int Swiss_Build_Int() {
        var d = new SwissTable<int, int>(Size);
        for (var i = 0; i < Size; i++) d.Add(i, i * 2);
        return d.Count;
    }

    [Benchmark(Description = "Frozen 构建 int")]
    public int Frozen_Build_Int() {
        var d = new Dictionary<int, int>(Size);
        for (var i = 0; i < Size; i++) d.Add(i, i * 2);
        return d.ToFrozenDictionary().Count;
    }

    // === 查找命中 — string key ===
    [Benchmark(Description = "Swiss 查找命中 str")]
    public int Swiss_LookupHit_Str() {
        var s = 0;
        foreach (var k in _strLookup) if (_swissStr.TryGetValue(k, out var v)) s += v;
        return s;
    }

    [Benchmark(Description = "Frozen 查找命中 str")]
    public int Frozen_LookupHit_Str() {
        var s = 0;
        foreach (var k in _strLookup) if (_frozenStr.TryGetValue(k, out var v)) s += v;
        return s;
    }

    // === 查找命中 — int key ===
    [Benchmark(Description = "Swiss 查找命中 int")]
    public int Swiss_LookupHit_Int() {
        var s = 0;
        foreach (var k in _intLookup) if (_swissInt.TryGetValue(k, out var v)) s += v;
        return s;
    }

    [Benchmark(Description = "Frozen 查找命中 int")]
    public int Frozen_LookupHit_Int() {
        var s = 0;
        foreach (var k in _intLookup) if (_frozenInt.TryGetValue(k, out var v)) s += v;
        return s;
    }

    // === 查找未命中 — string key ===
    [Benchmark(Description = "Swiss 查找未命中 str")]
    public int Swiss_LookupMiss_Str() {
        var s = 0;
        foreach (var k in _strMissing) if (_swissStr.TryGetValue(k, out var v)) s += v;
        return s;
    }

    [Benchmark(Description = "Frozen 查找未命中 str")]
    public int Frozen_LookupMiss_Str() {
        var s = 0;
        foreach (var k in _strMissing) if (_frozenStr.TryGetValue(k, out var v)) s += v;
        return s;
    }

    // === 查找未命中 — int key ===
    [Benchmark(Description = "Swiss 查找未命中 int")]
    public int Swiss_LookupMiss_Int() {
        var s = 0;
        foreach (var k in _intMissing) if (_swissInt.TryGetValue(k, out var v)) s += v;
        return s;
    }

    [Benchmark(Description = "Frozen 查找未命中 int")]
    public int Frozen_LookupMiss_Int() {
        var s = 0;
        foreach (var k in _intMissing) if (_frozenInt.TryGetValue(k, out var v)) s += v;
        return s;
    }

    // === ContainsKey — string key ===
    [Benchmark(Description = "Swiss ContainsKey str")]
    public int Swiss_ContainsKey_Str() {
        var s = 0;
        foreach (var k in _strLookup) if (_swissStr.ContainsKey(k)) s++;
        return s;
    }

    [Benchmark(Description = "Frozen ContainsKey str")]
    public int Frozen_ContainsKey_Str() {
        var s = 0;
        foreach (var k in _strLookup) if (_frozenStr.ContainsKey(k)) s++;
        return s;
    }

    // === 枚举 — string key ===
    [Benchmark(Description = "Swiss 枚举 str")]
    public int Swiss_Enumerate_Str() {
        var s = 0;
        foreach (var kv in _swissStr) s += kv.Value;
        return s;
    }

    [Benchmark(Description = "Frozen 枚举 str")]
    public int Frozen_Enumerate_Str() {
        var s = 0;
        foreach (var kv in _frozenStr) s += kv.Value;
        return s;
    }

    // === 枚举 — int key ===
    [Benchmark(Description = "Swiss 枚举 int")]
    public int Swiss_Enumerate_Int() {
        var s = 0;
        foreach (var kv in _swissInt) s += kv.Value;
        return s;
    }

    [Benchmark(Description = "Frozen 枚举 int")]
    public int Frozen_Enumerate_Int() {
        var s = 0;
        foreach (var kv in _frozenInt) s += kv.Value;
        return s;
    }
}
