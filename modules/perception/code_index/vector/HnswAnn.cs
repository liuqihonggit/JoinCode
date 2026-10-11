namespace JoinCode.CodeIndex.Vector;

/// <summary>
/// HNSW (Hierarchical Navigable Small World) 近似最近邻搜索实现。
/// <para>多层图结构：上层稀疏快速定位，下层密集精确搜索。</para>
/// <para>搜索复杂度 O(efSearch × log N)，比暴力搜索 O(N) 快数倍至数十倍。</para>
/// <para>线程安全：读操作（Search）读锁，写操作（Add/Remove）写锁。</para>
/// <para>使用 SwissTable（SIMD 加速字典）存储向量和图结构，PriorityQueue 做堆搜索。</para>
/// <para>实现 IAnnSearchGraphPersistence：加载时直接恢复邻接表，不重新构图（O(N) 而非 O(N × efConstruction × log N)）。</para>
/// </summary>
public sealed class HnswAnn : IAnnSearch, IAnnSearchGraphPersistence {

    private readonly SwissTable<string, float[]> _vectors = new();
    private readonly List<SwissTable<string, List<string>>> _layers = [];
    private string? _entryPoint;
    private int _entryLevel = -1;

    private readonly int _m;
    private readonly int _efConstruction;
    private readonly int _efSearch;
    private readonly double _mL;

    private readonly ReaderWriterLockSlim _lock = new();
    private readonly Random _rng = new();

    private const string GraphMagic = "HNSW1";

    /// <summary>
    /// 构造 HNSW 索引。
    /// </summary>
    /// <param name="m">每层最大连接数（第0层 = 2m）。默认 16。</param>
    /// <param name="efConstruction">建图时搜索宽度。默认 200。</param>
    /// <param name="efSearch">搜索时搜索宽度。默认 50。</param>
    public HnswAnn(int m = 16, int efConstruction = 200, int efSearch = 50) {
        if (m < 2) throw new ArgumentOutOfRangeException(nameof(m), "M must be >= 2");
        if (efConstruction < 1) throw new ArgumentOutOfRangeException(nameof(efConstruction));
        if (efSearch < 1) throw new ArgumentOutOfRangeException(nameof(efSearch));
        _m = m;
        _efConstruction = efConstruction;
        _efSearch = efSearch;
        _mL = 1.0 / Math.Log(m);
    }

    /// <summary>当前向量数量。</summary>
    public int Count {
        get {
            _lock.EnterReadLock();
            try {
                return _vectors.Count;
            } finally {
                _lock.ExitReadLock();
            }
        }
    }

    /// <summary>添加单个向量。若 id 已存在则覆盖。</summary>
    public void Add(string id, float[] vector) {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(vector);
        _lock.EnterWriteLock();
        try {
            InsertCore(id, vector);
        } finally {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>批量添加向量。已存在的 id 覆盖。</summary>
    public void AddRange(IReadOnlyList<(string Id, float[] Vector)> items) {
        ArgumentNullException.ThrowIfNull(items);
        _lock.EnterWriteLock();
        try {
            for (var i = 0; i < items.Count; i++)
                InsertCore(items[i].Id, items[i].Vector);
        } finally {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>删除指定 id 的向量。</summary>
    public void Remove(string id) {
        ArgumentNullException.ThrowIfNull(id);
        _lock.EnterWriteLock();
        try {
            if (!_vectors.ContainsKey(id)) return;
            _vectors.Remove(id);

            for (var lc = 0; lc < _layers.Count; lc++) {
                if (_layers[lc].TryGetValue(id, out var neighbors)) {
                    foreach (var n in neighbors) {
                        if (_layers[lc].TryGetValue(n, out var nNeighbors)) {
                            nNeighbors.Remove(id);
                        }
                    }
                    _layers[lc].Remove(id);
                }
            }

            if (_entryPoint == id) {
                _entryPoint = null;
                _entryLevel = -1;
                for (var lc = _layers.Count - 1; lc >= 0; lc--) {
                    if (_layers[lc].Count > 0) {
                        _entryPoint = _layers[lc].Keys.First();
                        _entryLevel = lc;
                        break;
                    }
                }
            }
        } finally {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// 搜索 top-K 最相似向量（余弦相似度）。
    /// </summary>
    public IReadOnlyList<(string Id, float Score)> Search(float[] query, int topK, CancellationToken ct) {
        ArgumentNullException.ThrowIfNull(query);
        if (topK <= 0) return [];

        _lock.EnterReadLock();
        try {
            if (_entryPoint is null || _vectors.Count == 0) return [];

            var ep = _entryPoint;
            for (var lc = _entryLevel; lc > 0; lc--) {
                var w = SearchLayer(query, ep, 1, lc, ct);
                if (w.Count > 0) ep = w[0].Id;
            }

            var ef = Math.Max(_efSearch, topK);
            var results = SearchLayer(query, ep, ef, 0, ct);
            return results
                .Select(x => (x.Id, Score: 1f - x.Dist))
                .OrderByDescending(x => x.Score)
                .Take(topK)
                .ToList();
        } finally {
            _lock.ExitReadLock();
        }
    }

    /// <summary>
    /// 保存图结构到 BinaryWriter — 邻接表+入口点+层数。
    /// <para>格式：HNSW1(magic) + entryLevel + entryPoint + layerCount + 每层邻接表。</para>
    /// </summary>
    public void SaveGraph(BinaryWriter bw) {
        ArgumentNullException.ThrowIfNull(bw);
        _lock.EnterReadLock();
        try {
            bw.Write(System.Text.Encoding.UTF8.GetBytes(GraphMagic));
            bw.Write(_entryLevel);
            WriteNullableString(bw, _entryPoint);
            bw.Write(_layers.Count);
            for (var lc = 0; lc < _layers.Count; lc++) {
                var layer = _layers[lc];
                bw.Write(layer.Count);
                foreach (var (id, neighbors) in layer) {
                    WriteString(bw, id);
                    bw.Write(neighbors.Count);
                    foreach (var n in neighbors) {
                        WriteString(bw, n);
                    }
                }
            }
        } finally {
            _lock.ExitReadLock();
        }
    }

    /// <summary>
    /// 从 BinaryReader 加载图结构，并用 vectors 填充内部向量存储。
    /// <para>直接恢复邻接表，不重新构图 — O(N) 加载而非 O(N × efConstruction × log N)。</para>
    /// </summary>
    public void LoadGraph(BinaryReader br, IReadOnlyDictionary<string, float[]> vectors) {
        ArgumentNullException.ThrowIfNull(br);
        ArgumentNullException.ThrowIfNull(vectors);
        var magic = System.Text.Encoding.UTF8.GetString(br.ReadBytes(GraphMagic.Length));
        if (magic != GraphMagic)
            throw new InvalidDataException($"HNSW 图格式不匹配: 期望 {GraphMagic}, 实际 {magic}");

        _lock.EnterWriteLock();
        try {
            _vectors.Clear();
            _layers.Clear();

            foreach (var (id, vec) in vectors) {
                _vectors[id] = vec;
            }

            _entryLevel = br.ReadInt32();
            _entryPoint = ReadNullableString(br);
            var layerCount = br.ReadInt32();
            for (var lc = 0; lc < layerCount; lc++) {
                var layer = new SwissTable<string, List<string>>();
                var nodeCount = br.ReadInt32();
                for (var n = 0; n < nodeCount; n++) {
                    var id = ReadString(br);
                    var neighborCount = br.ReadInt32();
                    var neighbors = new List<string>(neighborCount);
                    for (var nc = 0; nc < neighborCount; nc++) {
                        neighbors.Add(ReadString(br));
                    }
                    layer[id] = neighbors;
                }
                _layers.Add(layer);
            }
        } finally {
            _lock.ExitWriteLock();
        }
    }

    private void InsertCore(string id, float[] vector) {
        _vectors[id] = vector;

        if (_entryPoint is null) {
            _entryPoint = id;
            _entryLevel = 0;
            _layers.Add(new SwissTable<string, List<string>>());
            _layers[0][id] = [];
            return;
        }

        var l = (int)Math.Floor(-Math.Log(_rng.NextDouble() + double.Epsilon) * _mL);
        if (l < 0) l = 0;

        while (_layers.Count <= l)
            _layers.Add(new SwissTable<string, List<string>>());

        var ep = _entryPoint;
        for (var lc = _entryLevel; lc > l; lc--) {
            var w = SearchLayer(vector, ep, 1, lc, CancellationToken.None);
            if (w.Count > 0) ep = w[0].Id;
        }

        for (var lc = Math.Min(_entryLevel, l); lc >= 0; lc--) {
            var w = SearchLayer(vector, ep, _efConstruction, lc, CancellationToken.None);
            var neighbors = SelectNeighbors(vector, w, _m);

            if (!_layers[lc].TryGetValue(id, out var idNeighbors)) {
                idNeighbors = [];
                _layers[lc][id] = idNeighbors;
            }

            foreach (var (nId, _) in neighbors) {
                idNeighbors.Add(nId);

                if (!_layers[lc].TryGetValue(nId, out var nNeighbors)) {
                    nNeighbors = [];
                    _layers[lc][nId] = nNeighbors;
                }
                nNeighbors.Add(id);

                var maxM = lc == 0 ? 2 * _m : _m;
                if (nNeighbors.Count > maxM) {
                    var nVec = _vectors[nId];
                    var pruned = nNeighbors
                        .Select(x => (Id: x, Dist: Distance(nVec, _vectors[x])))
                        .OrderBy(x => x.Dist)
                        .Take(maxM)
                        .Select(x => x.Id)
                        .ToList();
                    _layers[lc][nId] = pruned;
                }
            }

            if (w.Count > 0) ep = w[0].Id;
        }

        if (l > _entryLevel) {
            _entryPoint = id;
            _entryLevel = l;
        }
    }

    private List<(string Id, float Dist)> SearchLayer(float[] query, string entryPoint, int ef, int lc, CancellationToken ct) {
        if (lc < 0 || lc >= _layers.Count) return [];
        if (!_vectors.TryGetValue(entryPoint, out var epVec)) return [];

        var visited = new HashSet<string> { entryPoint };
        var candidates = new PriorityQueue<string, float>();
        var results = new PriorityQueue<string, float>();

        var distEp = Distance(query, epVec);
        candidates.Enqueue(entryPoint, distEp);
        results.Enqueue(entryPoint, -distEp);

        var maxIterations = ef * 200;
        var iter = 0;
        while (candidates.Count > 0) {
            if (++iter > maxIterations) break;
            ct.ThrowIfCancellationRequested();
            if (!candidates.TryDequeue(out var c, out var distC)) break;
            if (c is null) break;
            results.TryPeek(out _, out var negDistF);
            var distF = -negDistF;

            if (distC > distF && results.Count >= ef) break;

            if (!_layers[lc].TryGetValue(c, out var neighbors)) continue;

            foreach (var e in neighbors) {
                if (!visited.Add(e)) continue;
                if (!_vectors.TryGetValue(e, out var eVec)) continue;

                results.TryPeek(out _, out negDistF);
                distF = -negDistF;
                var distE = Distance(query, eVec);

                if (distE < distF || results.Count < ef) {
                    candidates.Enqueue(e, distE);
                    results.Enqueue(e, -distE);
                    if (results.Count > ef) {
                        results.TryDequeue(out _, out _);
                    }
                }
            }
        }

        var output = new List<(string Id, float Dist)>(results.Count);
        while (results.Count > 0) {
            if (!results.TryDequeue(out var id, out var negDist)) break;
            if (id is null) continue;
            output.Add((id, -negDist));
        }
        return output;
    }

    private static List<(string Id, float Dist)> SelectNeighbors(float[] query, List<(string Id, float Dist)> candidates, int m) {
        return candidates.OrderBy(x => x.Dist).Take(m).ToList();
    }

    private static float Distance(float[] a, float[] b) {
        return 1f - VectorMath.CosineSimilarity(a, b);
    }

    private static void WriteString(BinaryWriter bw, string s) {
        var bytes = System.Text.Encoding.UTF8.GetBytes(s);
        bw.Write(bytes.Length);
        bw.Write(bytes);
    }

    private static void WriteNullableString(BinaryWriter bw, string? s) {
        if (s is null) { bw.Write(-1); return; }
        var bytes = System.Text.Encoding.UTF8.GetBytes(s);
        bw.Write(bytes.Length);
        bw.Write(bytes);
    }

    private static string ReadString(BinaryReader br) {
        var len = br.ReadInt32();
        return System.Text.Encoding.UTF8.GetString(br.ReadBytes(len));
    }

    private static string? ReadNullableString(BinaryReader br) {
        var len = br.ReadInt32();
        if (len < 0) return null;
        return System.Text.Encoding.UTF8.GetString(br.ReadBytes(len));
    }
}
