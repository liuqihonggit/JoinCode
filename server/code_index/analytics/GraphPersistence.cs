namespace JoinCode.CodeIndex.Analytics;

/// <summary>
/// 图持久化实现 — 将 InMemoryIndexStore 序列化为二进制文件 code-index.bin
/// </summary>
[Register(typeof(IBinaryPersistence), ServiceLifetime.Singleton)]
public sealed class GraphPersistence : ServiceEntity, IIndexStore {
    private readonly InMemoryIndexStore _store;
    private readonly IFileSystem _fs;
    private const int CurrentVersion = 1;
    private const string FileName = "code-index.bin";
    private static readonly byte[] Magic = System.Text.Encoding.UTF8.GetBytes("CGIDX1");

    /// <summary>
    /// 构造 GraphPersistence
    /// </summary>
    public GraphPersistence(InMemoryIndexStore store, IFileSystem fs) {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(fs);
        _store = store;
        _fs = fs;
    }

    /// <summary>索引类型标识。</summary>
    public IndexKind Kind => IndexKind.Symbol;

    /// <summary>当前符号数量。</summary>
    public int Count => _store.GetSnapshot().SymbolsByFqn.Count;

    /// <summary>是否已就绪（有符号即就绪）。</summary>
    public bool IsReady => Count > 0;

    /// <summary>
    /// 将索引存储序列化保存到指定目录的 code-index.bin 文件（二进制格式）
    /// </summary>
    public async Task SaveAsync(string directory, CancellationToken ct) {
        ArgumentNullException.ThrowIfNull(directory);

        var snap = _store.GetSnapshot();
        var symbols = snap.SymbolsByFqn.Values.ToList();
        var callEdges = snap.CallEdges.ToList();
        var depEdges = snap.DepEdges.ToList();
        var projects = snap.Projects.Values.ToList();
        var projectRefs = snap.ProjectRefs.Values.SelectMany(v => v).ToList();
        var nugetRefs = snap.NuGetRefs.Values.SelectMany(v => v).ToList();
        var fileTracking = snap.FileTracking.Values.Select(e => new FileTrackingInfo {
            FilePath = e.FilePath,
            Hash = e.Hash,
            SymbolCount = e.SymbolCount,
            LastModified = e.LastModified,
        }).ToList();

        await using var ms = new MemoryStream(estimateCapacity(symbols.Count, callEdges.Count, depEdges.Count));
        await using var bw = new BinaryWriter(ms, System.Text.Encoding.UTF8);

        bw.Write(Magic);
        bw.Write(CurrentVersion);
        WriteDateTimeOffset(bw, DateTimeOffset.UtcNow);

        bw.Write(symbols.Count);
        foreach (var s in symbols) {
            ct.ThrowIfCancellationRequested();
            bw.Write(s.Name);
            bw.Write(s.FullyQualifiedName);
            bw.Write((byte)s.Kind);
            bw.Write(s.FilePath);
            bw.Write(s.StartLine);
            bw.Write(s.EndLine);
            bw.Write(s.StartColumn);
            bw.Write(s.EndColumn);
            WriteNullableString(bw, s.ParentSymbol);
            WriteNullableString(bw, s.Namespace);
            WriteNullableString(bw, s.Accessibility);
        }

        bw.Write(callEdges.Count);
        foreach (var e in callEdges) {
            ct.ThrowIfCancellationRequested();
            bw.Write(e.CallerSymbol);
            bw.Write(e.CalleeSymbol);
            bw.Write(e.CallSiteFilePath);
            bw.Write(e.CallSiteLine);
            bw.Write((byte)e.CallKind);
        }

        bw.Write(depEdges.Count);
        foreach (var e in depEdges) {
            ct.ThrowIfCancellationRequested();
            bw.Write(e.SourceSymbol);
            bw.Write(e.TargetSymbol);
            bw.Write((byte)e.DependencyKind);
            WriteNullableString(bw, e.SourceFilePath);
        }

        bw.Write(projects.Count);
        foreach (var p in projects) {
            ct.ThrowIfCancellationRequested();
            bw.Write(p.Name);
            bw.Write(p.FilePath);
            WriteNullableString(bw, p.TargetFramework);
            WriteNullableString(bw, p.OutputType);
            WriteNullableString(bw, p.ProjectGuid);
        }

        bw.Write(projectRefs.Count);
        foreach (var r in projectRefs) {
            ct.ThrowIfCancellationRequested();
            bw.Write(r.SourceProjectPath);
            bw.Write(r.TargetProjectPath);
        }

        bw.Write(nugetRefs.Count);
        foreach (var r in nugetRefs) {
            ct.ThrowIfCancellationRequested();
            bw.Write(r.ProjectPath);
            bw.Write(r.PackageName);
            WriteNullableString(bw, r.Version);
        }

        bw.Write(fileTracking.Count);
        foreach (var ft in fileTracking) {
            ct.ThrowIfCancellationRequested();
            bw.Write(ft.FilePath);
            bw.Write(ft.Hash);
            bw.Write(ft.SymbolCount);
            WriteDateTimeOffset(bw, ft.LastModified);
        }

        bw.Flush();
        _fs.CreateDirectory(directory);
        var path = Path.Combine(directory, FileName);
        await _fs.WriteAllBytesAsync(path, ms.ToArray(), ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 从指定目录加载 code-index.bin 并重建索引存储
    /// </summary>
    public async Task<bool> LoadAsync(string directory, CancellationToken ct) {
        ArgumentNullException.ThrowIfNull(directory);
        var path = Path.Combine(directory, FileName);

        if (!_fs.FileExists(path))
            return false;

        var bytes = await _fs.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
        await using var ms = new MemoryStream(bytes, writable: false);
        using var br = new BinaryReader(ms, System.Text.Encoding.UTF8);

        var magic = br.ReadBytes(Magic.Length);
        if (!magic.SequenceEqual(Magic))
            return false;

        var version = br.ReadInt32();
        if (version != CurrentVersion)
            return false;

        var savedAt = ReadDateTimeOffset(br);

        var symbolCount = br.ReadInt32();
        var symbols = new List<SymbolInfo>(symbolCount);
        for (var i = 0; i < symbolCount; i++) {
            symbols.Add(new SymbolInfo {
                Name = br.ReadString(),
                FullyQualifiedName = br.ReadString(),
                Kind = (SymbolKind)br.ReadByte(),
                FilePath = br.ReadString(),
                StartLine = br.ReadInt32(),
                EndLine = br.ReadInt32(),
                StartColumn = br.ReadInt32(),
                EndColumn = br.ReadInt32(),
                ParentSymbol = ReadNullableString(br),
                Namespace = ReadNullableString(br),
                Accessibility = ReadNullableString(br),
            });
        }

        var callEdgeCount = br.ReadInt32();
        var callEdges = new List<CallEdge>(callEdgeCount);
        for (var i = 0; i < callEdgeCount; i++) {
            callEdges.Add(new CallEdge {
                CallerSymbol = br.ReadString(),
                CalleeSymbol = br.ReadString(),
                CallSiteFilePath = br.ReadString(),
                CallSiteLine = br.ReadInt32(),
                CallKind = (CallKind)br.ReadByte(),
            });
        }

        var depEdgeCount = br.ReadInt32();
        var depEdges = new List<DependencyEdge>(depEdgeCount);
        for (var i = 0; i < depEdgeCount; i++) {
            depEdges.Add(new DependencyEdge {
                SourceSymbol = br.ReadString(),
                TargetSymbol = br.ReadString(),
                DependencyKind = (DependencyKind)br.ReadByte(),
                SourceFilePath = ReadNullableString(br),
            });
        }

        var projectCount = br.ReadInt32();
        var projects = new List<ProjectInfo>(projectCount);
        for (var i = 0; i < projectCount; i++) {
            projects.Add(new ProjectInfo {
                Name = br.ReadString(),
                FilePath = br.ReadString(),
                TargetFramework = ReadNullableString(br),
                OutputType = ReadNullableString(br),
                ProjectGuid = ReadNullableString(br),
            });
        }

        var projRefCount = br.ReadInt32();
        var projRefs = new List<ProjectReferenceEdge>(projRefCount);
        for (var i = 0; i < projRefCount; i++) {
            projRefs.Add(new ProjectReferenceEdge {
                SourceProjectPath = br.ReadString(),
                TargetProjectPath = br.ReadString(),
            });
        }

        var nugetRefCount = br.ReadInt32();
        var nugetRefs = new List<NuGetPackageReference>(nugetRefCount);
        for (var i = 0; i < nugetRefCount; i++) {
            nugetRefs.Add(new NuGetPackageReference {
                ProjectPath = br.ReadString(),
                PackageName = br.ReadString(),
                Version = ReadNullableString(br),
            });
        }

        var ftCount = br.ReadInt32();
        var fileTracking = new List<FileTrackingInfo>(ftCount);
        for (var i = 0; i < ftCount; i++) {
            fileTracking.Add(new FileTrackingInfo {
                FilePath = br.ReadString(),
                Hash = br.ReadString(),
                SymbolCount = br.ReadInt32(),
                LastModified = ReadDateTimeOffset(br),
            });
        }

        var data = new GraphPersistenceData {
            Version = version,
            SavedAt = savedAt,
            Symbols = symbols,
            CallEdges = callEdges,
            DependencyEdges = depEdges,
            Projects = projects,
            ProjectReferences = projRefs,
            NuGetReferences = nugetRefs,
            FileTracking = fileTracking,
        };

        _store.Update(_ => IndexSnapshot.Load(data));
        return true;
    }

    /// <summary>
    /// 检查指定目录是否存在持久化索引文件
    /// </summary>
    public Task<bool> ExistsAsync(string directory, CancellationToken ct) {
        ArgumentNullException.ThrowIfNull(directory);
        var path = Path.Combine(directory, FileName);
        return Task.FromResult(_fs.FileExists(path));
    }

    private static void WriteNullableString(BinaryWriter bw, string? s) {
        if (s is null) {
            bw.Write((byte)0);
        } else {
            bw.Write((byte)1);
            bw.Write(s);
        }
    }

    private static string? ReadNullableString(BinaryReader br) {
        return br.ReadByte() == 0 ? null : br.ReadString();
    }

    private static void WriteDateTimeOffset(BinaryWriter bw, DateTimeOffset dto) {
        bw.Write(dto.UtcTicks);
        bw.Write((short)dto.Offset.TotalMinutes);
    }

    private static DateTimeOffset ReadDateTimeOffset(BinaryReader br) {
        var ticks = br.ReadInt64();
        var offsetMinutes = br.ReadInt16();
        return new DateTimeOffset(ticks, TimeSpan.FromMinutes(offsetMinutes));
    }

    private static int estimateCapacity(int symbols, int callEdges, int depEdges) {
        return 16 + symbols * 120 + callEdges * 80 + depEdges * 60;
    }
}
