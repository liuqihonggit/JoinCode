namespace StructuraBenchmarks;

/// <summary>ImmutableHamT vs BCL ImmutableDictionary 压测 — Stopwatch 快速对比,目标运行时长 3s 左右。</summary>
public static class Program {
    /// <summary>入口 — 对比 Lookup/Add/SetItem/Remove/Enumerate 五种操作在 10K~1M 规模下的性能。</summary>
    public static void Main() {
        var sizes = new[] { 1000, 10000, 50000, 100000 };
        Console.WriteLine($"{"Size",-10} {"Op",-12} {"BCL(ms)",-12} {"HAMT(ms)",-12} {"Ratio",-8}");
        Console.WriteLine(new string('-', 58));

        foreach (var size in sizes) {
            var data = new KeyValuePair<string, int>[size];
            for (var i = 0; i < size; i++) data[i] = new($"key_{i}", i);

            var lookupKeys = new KeyValuePair<string, int>[100000];
            var rnd = new Random(42);
            for (var i = 0; i < 100000; i++) lookupKeys[i] = data[rnd.Next(size)];

            var bclDict = ImmutableDictionary.Create<string, int>();
            var hamtDict = ImmutableHamT.Create<string, int>();
            foreach (var kv in data) { bclDict = bclDict.Add(kv.Key, kv.Value); hamtDict = hamtDict.Add(kv.Key, kv.Value); }

            Bench("Lookup", size, 50,
                () => { var s = 0; foreach (var kv in lookupKeys) if (bclDict.TryGetValue(kv.Key, out var v)) s += v; },
                () => { var s = 0; foreach (var kv in lookupKeys) if (hamtDict.TryGetValue(kv.Key, out var v)) s += v; });

            Bench("Add", size, 1,
                () => { var d = ImmutableDictionary.Create<string, int>(); foreach (var kv in data) d = d.Add(kv.Key, kv.Value); },
                () => { var d = ImmutableHamT.Create<string, int>(); foreach (var kv in data) d = d.Add(kv.Key, kv.Value); });

            Bench("SetItem", size, 10,
                () => { var d = bclDict; for (var i = 0; i < 1000; i++) d = d.SetItem($"key_{i}", i + 1000000); },
                () => { var d = hamtDict; for (var i = 0; i < 1000; i++) d = d.SetItem($"key_{i}", i + 1000000); });

            Bench("Remove", size, 10,
                () => { var d = bclDict; for (var i = 0; i < 1000; i++) d = d.Remove($"key_{i}"); },
                () => { var d = hamtDict; for (var i = 0; i < 1000; i++) d = d.Remove($"key_{i}"); });

            Bench("Enumerate", size, 5,
                () => { var s = 0; foreach (var kv in bclDict) s += kv.Value; },
                () => { var s = 0; foreach (var kv in hamtDict) s += kv.Value; });
        }

        Console.WriteLine();
        Console.WriteLine("=== ImmutableHamTSet vs ImmutableHashSet ===");
        Console.WriteLine($"{"Size",-10} {"Op",-12} {"BCL(ms)",-12} {"HAMT(ms)",-12} {"Ratio",-8}");
        Console.WriteLine(new string('-', 58));

        foreach (var size in sizes) {
            var keys = new string[size];
            for (var i = 0; i < size; i++) keys[i] = $"key_{i}";

            var lookupKeys = new string[100000];
            var rnd = new Random(42);
            for (var i = 0; i < 100000; i++) lookupKeys[i] = keys[rnd.Next(size)];

            var bclSet = ImmutableHashSet.Create<string>();
            var hamtSet = ImmutableHamTSet.Create<string>();
            foreach (var k in keys) { bclSet = bclSet.Add(k); hamtSet = hamtSet.Add(k); }

            BenchSet("Contains", size, 50,
                () => { var s = 0; foreach (var k in lookupKeys) if (bclSet.Contains(k)) s++; },
                () => { var s = 0; foreach (var k in lookupKeys) if (hamtSet.Contains(k)) s++; });

            BenchSet("Add", size, 1,
                () => { var d = ImmutableHashSet.Create<string>(); foreach (var k in keys) d = d.Add(k); },
                () => { var d = ImmutableHamTSet.Create<string>(); foreach (var k in keys) d = d.Add(k); });

            BenchSet("Remove", size, 10,
                () => { var d = bclSet; for (var i = 0; i < 1000; i++) d = d.Remove($"key_{i}"); },
                () => { var d = hamtSet; for (var i = 0; i < 1000; i++) d = d.Remove($"key_{i}"); });

            BenchSet("Enumerate", size, 5,
                () => { var s = 0; foreach (var k in bclSet) s += k.Length; },
                () => { var s = 0; foreach (var k in hamtSet) s += k.Length; });
        }
    }

    static void BenchSet(string op, int size, int iterations, Action bcl, Action hamt) {
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++) bcl();
        sw.Stop();
        var bclMs = sw.ElapsedMilliseconds;

        sw.Restart();
        for (var i = 0; i < iterations; i++) hamt();
        sw.Stop();
        var hamtMs = sw.ElapsedMilliseconds;

        var ratio = bclMs == 0 ? "N/A" : $"{(double)hamtMs / bclMs:F2}x";
        Console.WriteLine($"{size,-10} {op,-12} {bclMs,-12} {hamtMs,-12} {ratio,-8}");
    }

    static void Bench(string op, int size, int iterations, Action bcl, Action hamt) {
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++) bcl();
        sw.Stop();
        var bclMs = sw.ElapsedMilliseconds;

        sw.Restart();
        for (var i = 0; i < iterations; i++) hamt();
        sw.Stop();
        var hamtMs = sw.ElapsedMilliseconds;

        var ratio = bclMs == 0 ? "N/A" : $"{(double)hamtMs / bclMs:F2}x";
        Console.WriteLine($"{size,-10} {op,-12} {bclMs,-12} {hamtMs,-12} {ratio,-8}");
    }
}
