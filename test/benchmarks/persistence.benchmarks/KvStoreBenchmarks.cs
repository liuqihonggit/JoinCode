namespace PersistenceBenchmarks;

public class KvStoreBenchmarks {
    private PithosKvStore _pithos = null!;
    private InMemoryKvStore _inMem = null!;
    private byte[][] _keys = null!;
    private byte[][] _values = null!;
    private int _index;

    [Params(100, 1000, 10000)]
    public int Count { get; set; }

    [GlobalSetup]
    public void Setup() {
        var dir = Path.Combine(Path.GetTempPath(), $"pithos_bench_{Guid.NewGuid():N}");
        _pithos = new PithosKvStore(dir);
        _inMem = new InMemoryKvStore();
        _keys = new byte[Count][];
        _values = new byte[Count][];
        for (var i = 0; i < Count; i++) {
            _keys[i] = Encoding.UTF8.GetBytes($"key:{i:D8}");
            _values[i] = Encoding.UTF8.GetBytes($"value:{i}:padding-padding-padding-padding");
        }
    }

    [GlobalCleanup]
    public async Task Cleanup() {
        await _pithos.DisposeAsync();
        await _inMem.DisposeAsync();
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Put")]
    public async Task Put_Pithos() {
        var i = _index++ % Count;
        await _pithos.PutAsync(_keys[i], _values[i]);
    }

    [Benchmark]
    [BenchmarkCategory("Put")]
    public async Task Put_InMemory() {
        var i = _index++ % Count;
        await _inMem.PutAsync(_keys[i], _values[i]);
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Get")]
    public async Task<byte[]?> Get_Pithos() {
        var i = _index++ % Count;
        return await _pithos.GetAsync(_keys[i]);
    }

    [Benchmark]
    [BenchmarkCategory("Get")]
    public async Task<byte[]?> Get_InMemory() {
        var i = _index++ % Count;
        return await _inMem.GetAsync(_keys[i]);
    }
}

public class KvStoreBulkBenchmarks {
    private PithosKvStore _pithos = null!;

    [Params(100, 1000, 10000)]
    public int Count { get; set; }

    [IterationSetup]
    public void Setup() {
        var dir = Path.Combine(Path.GetTempPath(), $"pithos_bulk_{Guid.NewGuid():N}");
        _pithos = new PithosKvStore(dir);
    }

    [IterationCleanup]
    public async Task Cleanup() {
        await _pithos.DisposeAsync();
    }

    [Benchmark]
    public async Task BulkPut_Pithos() {
        for (var i = 0; i < Count; i++) {
            var key = Encoding.UTF8.GetBytes($"key:{i:D8}");
            var val = Encoding.UTF8.GetBytes($"val:{i}");
            await _pithos.PutAsync(key, val);
        }
    }

    [Benchmark]
    public async Task BulkScan_Pithos() {
        var count = 0;
        await foreach (var _ in _pithos.ScanAsync()) {
            count++;
        }
    }
}

public static class Program {
    public static void Main(string[] args) {
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
    }
}
