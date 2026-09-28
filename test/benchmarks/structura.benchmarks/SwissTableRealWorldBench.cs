namespace StructuraBenchmarks;

/// <summary>SwissTable 真实负载压测 — 查找密集/混合操作/int key/高碰撞场景。</summary>
[MemoryDiagnoser]
[ShortRunJob]
public class SwissTableRealWorldBench {

    // 场景1: 查找密集 — 构建一次，查找百万次（配置表/缓存模式）
    private Dictionary<string, int> _bclStr = null!;
    private SwissTable<string, int> _swissStr = null!;
    private string[] _strKeys = null!;
    private string[] _strLookup = null!;

    // 场景2: int key 查找密集
    private Dictionary<int, int> _bclInt = null!;
    private SwissTable<int, int> _swissInt = null!;
    private int[] _intLookup = null!;

    // 场景3: 混合操作 — Add+Lookup+Remove 交替
    private string[] _mixKeys = null!;

    [Params(50000)]
    public int Size { get; set; }

    [GlobalSetup]
    public void Setup() {
        // string key 准备
        _strKeys = new string[Size];
        for (var i = 0; i < Size; i++) _strKeys[i] = $"key_{i}";
        _strLookup = new string[500000];
        var rnd = new Random(42);
        for (var i = 0; i < 500000; i++) _strLookup[i] = _strKeys[rnd.Next(Size)];

        _bclStr = new Dictionary<string, int>(Size);
        _swissStr = new SwissTable<string, int>(Size);
        foreach (var k in _strKeys) { _bclStr.Add(k, k.Length); _swissStr.Add(k, k.Length); }

        // int key 准备
        _bclInt = new Dictionary<int, int>(Size);
        _swissInt = new SwissTable<int, int>(Size);
        for (var i = 0; i < Size; i++) { _bclInt.Add(i, i * 2); _swissInt.Add(i, i * 2); }
        _intLookup = new int[500000];
        for (var i = 0; i < 500000; i++) _intLookup[i] = rnd.Next(Size);

        // 混合操作准备
        _mixKeys = new string[Size];
        for (var i = 0; i < Size; i++) _mixKeys[i] = $"mix_{i}";
    }

    // === 场景1: 查找密集 (string key) ===
    [Benchmark(Description = "BCL 查找密集 str")]
    public int Bcl_LookupHeavy_Str() {
        var s = 0;
        foreach (var k in _strLookup) if (_bclStr.TryGetValue(k, out var v)) s += v;
        return s;
    }

    [Benchmark(Description = "Swiss 查找密集 str")]
    public int Swiss_LookupHeavy_Str() {
        var s = 0;
        foreach (var k in _strLookup) if (_swissStr.TryGetValue(k, out var v)) s += v;
        return s;
    }

    // === 场景2: 查找密集 (int key) ===
    [Benchmark(Description = "BCL 查找密集 int")]
    public int Bcl_LookupHeavy_Int() {
        var s = 0;
        foreach (var k in _intLookup) if (_bclInt.TryGetValue(k, out var v)) s += v;
        return s;
    }

    [Benchmark(Description = "Swiss 查找密集 int")]
    public int Swiss_LookupHeavy_Int() {
        var s = 0;
        foreach (var k in _intLookup) if (_swissInt.TryGetValue(k, out var v)) s += v;
        return s;
    }

    // === 场景3: 混合操作 — Add 50% + Lookup 30% + Remove 20% ===
    [Benchmark(Description = "BCL 混合操作")]
    public int Bcl_MixedOps() {
        var d = new Dictionary<string, int>(Size);
        var s = 0;
        var rnd = new Random(123);
        for (var i = 0; i < Size; i++) {
            var op = rnd.Next(10);
            var key = _mixKeys[rnd.Next(Size)];
            if (op < 5) d[key] = i;                    // 50% Add/Set
            else if (op < 8) { if (d.TryGetValue(key, out var v)) s += v; }  // 30% Lookup
            else d.Remove(key);                         // 20% Remove
        }
        return s + d.Count;
    }

    [Benchmark(Description = "Swiss 混合操作")]
    public int Swiss_MixedOps() {
        var d = new SwissTable<string, int>(Size);
        var s = 0;
        var rnd = new Random(123);
        for (var i = 0; i < Size; i++) {
            var op = rnd.Next(10);
            var key = _mixKeys[rnd.Next(Size)];
            if (op < 5) d[key] = i;
            else if (op < 8) { if (d.TryGetValue(key, out var v)) s += v; }
            else d.Remove(key);
        }
        return s + d.Count;
    }

    // === 场景4: 渐进构建 + 频繁查找（模拟请求跟踪表，小规模防超时）===
    [Benchmark(Description = "BCL 渐进构建+查找")]
    public int Bcl_ProgressiveBuildAndLookup() {
        var n = 10000;
        var d = new Dictionary<string, int>(n / 4);
        var s = 0;
        var rnd = new Random(456);
        for (var i = 0; i < n; i++) {
            var key = _strKeys[i % (n / 4)];
            d[key] = i;
            if (i % 4 == 0) { for (var j = 0; j < 8; j++) { var lk = _strKeys[rnd.Next(n / 4)]; if (d.TryGetValue(lk, out var v)) s += v; } }
        }
        return s + d.Count;
    }

    [Benchmark(Description = "Swiss 渐进构建+查找")]
    public int Swiss_ProgressiveBuildAndLookup() {
        var n = 10000;
        var d = new SwissTable<string, int>(n / 4);
        var s = 0;
        var rnd = new Random(456);
        for (var i = 0; i < n; i++) {
            var key = _strKeys[i % (n / 4)];
            d[key] = i;
            if (i % 4 == 0) { for (var j = 0; j < 8; j++) { var lk = _strKeys[rnd.Next(n / 4)]; if (d.TryGetValue(lk, out var v)) s += v; } }
        }
        return s + d.Count;
    }

    // === 场景5: 高碰撞 — 自定义 comparer 让所有 key 映射到少数桶（小规模防超时）===
    [Benchmark(Description = "BCL 高碰撞")]
    public int Bcl_HighCollision() {
        var cmp = new HighCollisionComparer();
        var n = 2000;
        var d = new Dictionary<string, int>(n, cmp);
        for (var i = 0; i < n; i++) d[_strKeys[i]] = i;
        var s = 0;
        for (var i = 0; i < 50000; i++) if (d.TryGetValue(_strKeys[i % n], out var v)) s += v;
        return s;
    }

    [Benchmark(Description = "Swiss 高碰撞")]
    public int Swiss_HighCollision() {
        var cmp = new HighCollisionComparer();
        var n = 2000;
        var d = new SwissTable<string, int>(n, cmp);
        for (var i = 0; i < n; i++) d[_strKeys[i]] = i;
        var s = 0;
        for (var i = 0; i < 50000; i++) if (d.TryGetValue(_strKeys[i % n], out var v)) s += v;
        return s;
    }

    /// <summary>高碰撞 comparer — 只用 key 长度取模 8 作为哈希，强制大量碰撞。</summary>
    private sealed class HighCollisionComparer : IEqualityComparer<string> {
        public bool Equals(string? x, string? y) => string.Equals(x, y, StringComparison.Ordinal);
        public int GetHashCode(string obj) => obj.Length & 7;
    }
}
