namespace JoinCode.CodeIndex.Analytics;

/// <summary>
/// 图持久化实现 — 将 InMemoryIndexStore 序列化为 JSON 文件
/// </summary>
[Register(typeof(IGraphPersistence), ServiceLifetime.Singleton)]
public sealed class GraphPersistence : ServiceEntity, IGraphPersistence {
    private readonly InMemoryIndexStore _store;
    private readonly IFileSystem _fs;
    private readonly IPersistencePipeline? _pipeline;
    private const int CurrentVersion = 1;

    /// <summary>
    /// 构造 GraphPersistence
    /// </summary>
    public GraphPersistence(InMemoryIndexStore store, IFileSystem fs, IPersistencePipeline? pipeline = null) {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(fs);
        _store = store;
        _fs = fs;
        _pipeline = pipeline;
    }

    /// <summary>
    /// 将索引存储序列化保存到指定目录的 code-index.json 文件
    /// </summary>
    public async Task SaveAsync(string directory, CancellationToken ct) {
        ArgumentNullException.ThrowIfNull(directory);

        var snap = _store.GetSnapshot();
        var data = new GraphPersistenceData {
            Version = CurrentVersion,
            SavedAt = DateTimeOffset.UtcNow,
            Symbols = snap.SymbolsByFqn.Values.ToList(),
            CallEdges = snap.CallEdges.ToList(),
            DependencyEdges = snap.DepEdges.ToList(),
            Projects = snap.Projects.Values.ToList(),
            ProjectReferences = snap.ProjectRefs.Values.SelectMany(v => v).ToList(),
            NuGetReferences = snap.NuGetRefs.Values.SelectMany(v => v).ToList(),
            FileTracking = snap.FileTracking.Values.Select(e => new FileTrackingInfo {
                FilePath = e.FilePath,
                Hash = e.Hash,
                SymbolCount = e.SymbolCount,
                LastModified = e.LastModified,
            }).ToList(),
        };
        var json = RelaxedJsonSerializer.Serialize(data, CodeIndexJsonContext.Default);

        var fileName = "code-index.json";

        if (_pipeline is not null) {
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var request = new PersistRequest {
                Category = "code_index",
                Directory = directory,
                FileName = fileName,
                Content = json,
                Completion = tcs,
            };
            await _pipeline.EnqueueAsync(request, ct).ConfigureAwait(false);
            await tcs.Task.ConfigureAwait(false);
        } else {
            _fs.CreateDirectory(directory);
            var path = Path.Combine(directory, fileName);
            await _fs.WriteAllTextAsync(path, json, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 从指定目录加载 code-index.json 并重建索引存储
    /// </summary>
    public async Task<bool> LoadAsync(string directory, CancellationToken ct) {
        ArgumentNullException.ThrowIfNull(directory);
        var path = Path.Combine(directory, "code-index.json");

        if (!_fs.FileExists(path))
            return false;

        var json = await _fs.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        var data = RelaxedJsonSerializer.Deserialize(json, CodeIndexJsonContext.Default.GraphPersistenceData);

        if (data is null || data.Version != CurrentVersion)
            return false;

        _store.Update(_ => IndexSnapshot.Load(data));
        return true;
    }

    /// <summary>
    /// 检查指定目录是否存在持久化索引文件
    /// </summary>
    public Task<bool> ExistsAsync(string directory, CancellationToken ct) {
        ArgumentNullException.ThrowIfNull(directory);
        var path = Path.Combine(directory, "code-index.json");
        return Task.FromResult(_fs.FileExists(path));
    }
}
