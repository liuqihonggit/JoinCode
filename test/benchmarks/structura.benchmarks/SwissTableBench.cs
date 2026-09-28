namespace StructuraBenchmarks;

/// <summary>SwissTable vs BCL Dictionary 压测 — 可变哈希表性能对比,覆盖 Add/Lookup/Remove/Enumerate。</summary>
[MemoryDiagnoser]
[ShortRunJob]
public class SwissTableBench {
    private Dictionary<string, int> _bclDict = null!;
    private SwissTable<string, int> _swissTable = null!;
    private string[] _keys = null!;
    private string[] _lookupKeys = null!;

    [Params(10000, 100000)]
    public int Size { get; set; }

    [GlobalSetup]
    public void Setup() {
        _keys = new string[Size];
        for (var i = 0; i < Size; i++) _keys[i] = $"key_{i}";

        _lookupKeys = new string[100000];
        var rnd = new Random(42);
        for (var i = 0; i < 100000; i++) _lookupKeys[i] = _keys[rnd.Next(Size)];

        _bclDict = new Dictionary<string, int>(Size);
        _swissTable = new SwissTable<string, int>(Size);
        foreach (var k in _keys) { _bclDict.Add(k, k.Length); _swissTable.Add(k, k.Length); }
    }

    [Benchmark(Description = "BCL Dict Lookup")]
    public int Bcl_Lookup() {
        var s = 0;
        foreach (var k in _lookupKeys) if (_bclDict.TryGetValue(k, out var v)) s += v;
        return s;
    }

    [Benchmark(Description = "SwissTable Lookup")]
    public int Swiss_Lookup() {
        var s = 0;
        foreach (var k in _lookupKeys) if (_swissTable.TryGetValue(k, out var v)) s += v;
        return s;
    }

    [Benchmark(Description = "BCL Dict Add")]
    public int Bcl_Add() {
        var d = new Dictionary<string, int>(Size);
        for (var i = 0; i < Size; i++) d.Add(_keys[i], i);
        return d.Count;
    }

    [Benchmark(Description = "SwissTable Add")]
    public int Swiss_Add() {
        var d = new SwissTable<string, int>(Size);
        for (var i = 0; i < Size; i++) d.Add(_keys[i], i);
        return d.Count;
    }

    [Benchmark(Description = "BCL Dict Remove")]
    public int Bcl_Remove() {
        var d = new Dictionary<string, int>(_bclDict);
        for (var i = 0; i < Size / 2; i++) d.Remove(_keys[i]);
        return d.Count;
    }

    [Benchmark(Description = "SwissTable Remove")]
    public int Swiss_Remove() {
        var d = new SwissTable<string, int>(_swissTable);
        for (var i = 0; i < Size / 2; i++) d.Remove(_keys[i]);
        return d.Count;
    }

    [Benchmark(Description = "BCL Dict Enumerate")]
    public int Bcl_Enumerate() {
        var s = 0;
        foreach (var kv in _bclDict) s += kv.Value;
        return s;
    }

    [Benchmark(Description = "SwissTable Enumerate")]
    public int Swiss_Enumerate() {
        var s = 0;
        foreach (var kv in _swissTable) s += kv.Value;
        return s;
    }

    [Benchmark(Description = "BCL Dict ContainsKey")]
    public int Bcl_ContainsKey() {
        var s = 0;
        foreach (var k in _lookupKeys) if (_bclDict.ContainsKey(k)) s++;
        return s;
    }

    [Benchmark(Description = "SwissTable ContainsKey")]
    public int Swiss_ContainsKey() {
        var s = 0;
        foreach (var k in _lookupKeys) if (_swissTable.ContainsKey(k)) s++;
        return s;
    }
}
