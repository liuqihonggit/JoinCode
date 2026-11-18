namespace Services.Lsp.Internal;

/// <summary>
/// LSP 诊断项 — 单条诊断消息（含严重级别、范围、来源、代码）
/// </summary>
public sealed class LspDiagnosticItem {
    /// <summary>诊断消息文本</summary>
    public required string Message { get; init; }
    /// <summary>严重级别（Error/Warning/Info/Hint）</summary>
    public string? Severity { get; init; }
    /// <summary>诊断范围（起止位置）</summary>
    public LspRange? Range { get; init; }
    /// <summary>诊断来源（如服务器名）</summary>
    public string? Source { get; init; }
    /// <summary>诊断代码</summary>
    public string? Code { get; init; }
}

/// <summary>
/// LSP 诊断文件 — 单个文件 URI 下的诊断列表
/// </summary>
public sealed class LspDiagnosticFile {
    /// <summary>文件 URI</summary>
    public required string Uri { get; init; }
    /// <summary>该文件的诊断列表</summary>
    public required List<LspDiagnosticItem> Diagnostics { get; set; } = [];
}

/// <summary>
/// LSP 待处理诊断 — 来自某服务器的待发送诊断批次
/// </summary>
public sealed class LspPendingDiagnostic {
    /// <summary>服务器名称</summary>
    public required string ServerName { get; init; }
    /// <summary>待发送的文件诊断列表</summary>
    public required List<LspDiagnosticFile> Files { get; init; }
    /// <summary>时间戳（毫秒）</summary>
    public long Timestamp { get; init; }
}

/// <summary>
/// LSP 诊断注册表接口 — 管理待处理诊断的注册、检查、清理与重置
/// </summary>
public interface ILspDiagnosticRegistry : IRegistry {
    /// <summary>
    /// 注册待处理诊断
    /// </summary>
    /// <param name="serverName">服务器名称</param>
    /// <param name="files">文件诊断列表</param>
    void RegisterPending(string serverName, List<LspDiagnosticFile> files);

    /// <summary>
    /// 检查并取出待处理诊断 — 返回服务器名与文件诊断列表的元组
    /// </summary>
    /// <returns>待处理诊断列表</returns>
    List<(string ServerName, List<LspDiagnosticFile> Files)> CheckPending();

    /// <summary>清空所有待处理诊断</summary>
    void ClearAll();

    /// <summary>重置所有状态（待处理与已投递）</summary>
    void ResetAll();

    /// <summary>
    /// 清除指定文件的已投递记录
    /// </summary>
    /// <param name="fileUri">文件 URI</param>
    void ClearDeliveredForFile(string fileUri);

    /// <summary>待处理诊断数量</summary>
    int PendingCount { get; }
}

/// <summary>
/// LSP 诊断注册表实现 — 维护待处理诊断、已投递去重与 LRU 容量限制
/// </summary>
[Register(typeof(ILspDiagnosticRegistry), ServiceLifetime.Singleton)]
[Register(typeof(JoinCode.Abstractions.Interfaces.Lsp.ILspDiagnosticProvider), ServiceLifetime.Singleton)]
public sealed partial class LspDiagnosticRegistry : ServiceEntity, ILspDiagnosticRegistry, JoinCode.Abstractions.Interfaces.Lsp.ILspDiagnosticProvider {

    /// <summary>
    /// 构造函数 — 注入时钟服务
    /// </summary>
    /// <param name="clock">时钟服务</param>
    public LspDiagnosticRegistry(IClockService clock) {
        _clock = clock;
    }
    private const int MaxDiagnosticsPerFile = 10;
    private const int MaxTotalDiagnostics = 30;
    private const int MaxDeliveredFiles = 500;

    private readonly IClockService _clock;
    private LspDiagnosticRegistryState _state = LspDiagnosticRegistryState.Empty;

    /// <summary>注册表不可变快照 — 待处理批次、已投递去重键与投递顺序</summary>
    private sealed record LspDiagnosticRegistryState(
        ImmutableHamT<string, LspPendingDiagnostic> Pending,
        ImmutableHamT<string, ImmutableHamTSet<string>> Delivered,
        ImmutableList<string> DeliveredOrder) {

        /// <summary>空状态</summary>
        public static LspDiagnosticRegistryState Empty { get; } = new(
            ImmutableHamT<string, LspPendingDiagnostic>.Empty,
            ImmutableHamT<string, ImmutableHamTSet<string>>.Empty,
            ImmutableList<string>.Empty);
    }

    /// <summary>待处理诊断数量</summary>
    public int PendingCount => Volatile.Read(ref _state).Pending.Count;

    /// <summary>
    /// 注册待处理诊断
    /// </summary>
    /// <param name="serverName">服务器名称</param>
    /// <param name="files">文件诊断列表</param>
    public void RegisterPending(string serverName, List<LspDiagnosticFile> files) {
        if (files.Count == 0) return;

        var id = Guid.NewGuid().ToString("N");
        var diagnostic = new LspPendingDiagnostic {
            ServerName = serverName,
            Files = files,
            Timestamp = _clock.GetUtcNowOffset().ToUnixTimeMilliseconds()
        };

        Mutate(state => state with { Pending = state.Pending.Add(id, diagnostic) });
    }

    /// <summary>
    /// 检查并取出待处理诊断 — 去重、容量限制、追踪已投递
    /// </summary>
    /// <returns>服务器名与文件诊断列表的元组列表</returns>
    public List<(string ServerName, List<LspDiagnosticFile> Files)> CheckPending() {
        List<LspDiagnosticFile> allFiles;
        HashSet<string> serverNames;
        ImmutableHamT<string, ImmutableHamTSet<string>> deliveredSnapshot;

        while (true) {
            var current = Volatile.Read(ref _state);
            if (current.Pending.Count == 0) return [];

            allFiles = [];
            serverNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var diag in current.Pending.Values) {
                allFiles.AddRange(diag.Files);
                serverNames.Add(diag.ServerName);
            }

            var next = current with { Pending = ImmutableHamT<string, LspPendingDiagnostic>.Empty };
            if (Interlocked.CompareExchange(ref _state, next, current) == current) {
                deliveredSnapshot = current.Delivered;
                break;
            }
        }

        if (allFiles.Count == 0) return [];

        var dedupedFiles = DeduplicateDiagnosticFiles(allFiles, deliveredSnapshot);

        ApplyVolumeLimits(dedupedFiles);

        TrackDelivered(dedupedFiles);

        if (dedupedFiles.Count == 0) return [];

        return
        [
            (string.Join(", ", serverNames), dedupedFiles)
        ];
    }

    /// <summary>清空所有待处理诊断</summary>
    public void ClearAll() {
        Mutate(state => state.Pending.Count == 0
            ? state
            : state with { Pending = ImmutableHamT<string, LspPendingDiagnostic>.Empty });
    }

    /// <summary>重置所有状态（待处理与已投递）</summary>
    public void ResetAll() {
        Interlocked.Exchange(ref _state, LspDiagnosticRegistryState.Empty);
    }

    /// <summary>
    /// 清除指定文件的已投递记录
    /// </summary>
    /// <param name="fileUri">文件 URI</param>
    public void ClearDeliveredForFile(string fileUri) {
        Mutate(state => {
            if (!state.Delivered.ContainsKey(fileUri)) return state;

            var index = state.DeliveredOrder.IndexOf(fileUri);
            return state with {
                Delivered = state.Delivered.Remove(fileUri),
                DeliveredOrder = index >= 0 ? state.DeliveredOrder.RemoveAt(index) : state.DeliveredOrder
            };
        });
    }

    /// <summary>CAS 发布状态变更 — mutate 返回原实例表示无变更</summary>
    private void Mutate(Func<LspDiagnosticRegistryState, LspDiagnosticRegistryState> mutate) {
        while (true) {
            var current = Volatile.Read(ref _state);
            var next = mutate(current);
            if (ReferenceEquals(next, current)) return;
            if (Interlocked.CompareExchange(ref _state, next, current) == current) return;
        }
    }

    /// <summary>
    /// 检查并取出待处理诊断摘要 — 转换为 LspDiagnosticSummary 形式
    /// </summary>
    /// <returns>服务器名与诊断摘要列表的元组列表</returns>
    public List<(string ServerName, List<JoinCode.Abstractions.Interfaces.Lsp.LspDiagnosticSummary> Files)> CheckPendingDiagnostics() {
        var pending = CheckPending();
        var result = new List<(string ServerName, List<JoinCode.Abstractions.Interfaces.Lsp.LspDiagnosticSummary> Files)>();

        foreach (var (serverName, files) in pending) {
            var summaries = files.Select(f => new JoinCode.Abstractions.Interfaces.Lsp.LspDiagnosticSummary {
                Uri = f.Uri,
                Diagnostics = f.Diagnostics.Select(d => new JoinCode.Abstractions.Interfaces.Lsp.LspDiagnosticEntry {
                    Message = d.Message,
                    Severity = d.Severity,
                    StartLine = d.Range?.Start.Line,
                    StartCharacter = d.Range?.Start.Character,
                    Source = d.Source,
                    Code = d.Code
                }).ToList()
            }).ToList();

            result.Add((serverName, summaries));
        }

        return result;
    }

    private static List<LspDiagnosticFile> DeduplicateDiagnosticFiles(
        List<LspDiagnosticFile> allFiles,
        ImmutableHamT<string, ImmutableHamTSet<string>> delivered) {
        var fileMap = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var dedupedFileMap = new Dictionary<string, LspDiagnosticFile>(StringComparer.OrdinalIgnoreCase);
        var dedupedFiles = new List<LspDiagnosticFile>();

        foreach (var file in allFiles) {
            if (!fileMap.ContainsKey(file.Uri)) {
                fileMap[file.Uri] = new HashSet<string>(StringComparer.Ordinal);
                var newDedupedFile = new LspDiagnosticFile { Uri = file.Uri, Diagnostics = [] };
                dedupedFiles.Add(newDedupedFile);
                dedupedFileMap[file.Uri] = newDedupedFile;
            }

            var seenDiagnostics = fileMap[file.Uri];
            var dedupedFile = dedupedFileMap[file.Uri];

            delivered.TryGetValue(file.Uri, out var previouslyDelivered);

            foreach (var diag in file.Diagnostics) {
                var key = CreateDiagnosticKey(diag);

                if (seenDiagnostics.Contains(key) || (previouslyDelivered is not null && previouslyDelivered.Contains(key))) {
                    continue;
                }

                seenDiagnostics.Add(key);
                dedupedFile.Diagnostics.Add(diag);
            }
        }

        return dedupedFiles.Where(f => f.Diagnostics.Count > 0).ToList();
    }

    private static void ApplyVolumeLimits(List<LspDiagnosticFile> files) {
        var totalDiagnostics = 0;
        var truncatedCount = 0;

        foreach (var file in files) {
            file.Diagnostics.Sort((a, b) => SeverityToNumber(a.Severity) - SeverityToNumber(b.Severity));

            if (file.Diagnostics.Count > MaxDiagnosticsPerFile) {
                truncatedCount += file.Diagnostics.Count - MaxDiagnosticsPerFile;
                file.Diagnostics = file.Diagnostics[..MaxDiagnosticsPerFile];
            }

            var remainingCapacity = MaxTotalDiagnostics - totalDiagnostics;
            if (file.Diagnostics.Count > remainingCapacity) {
                truncatedCount += file.Diagnostics.Count - remainingCapacity;
                file.Diagnostics = file.Diagnostics[..remainingCapacity];
            }

            totalDiagnostics += file.Diagnostics.Count;
        }

        files.RemoveAll(f => f.Diagnostics.Count == 0);
    }

    private void TrackDelivered(List<LspDiagnosticFile> files) {
        if (files.Count == 0) return;

        Mutate(state => {
            var delivered = state.Delivered;
            var order = state.DeliveredOrder;

            foreach (var file in files) {
                if (!delivered.ContainsKey(file.Uri)) {
                    order = order.Add(file.Uri);
                }

                var keys = delivered.TryGetValue(file.Uri, out var existing)
                    ? existing
                    : ImmutableHamTSet<string>.Empty;

                foreach (var diag in file.Diagnostics) {
                    keys = keys.Add(CreateDiagnosticKey(diag));
                }

                delivered = delivered.SetItem(file.Uri, keys);

                while (delivered.Count > MaxDeliveredFiles) {
                    var oldest = order[0];
                    order = order.RemoveAt(0);
                    delivered = delivered.Remove(oldest);
                }
            }

            if (ReferenceEquals(delivered, state.Delivered) && ReferenceEquals(order, state.DeliveredOrder)) {
                return state;
            }

            return state with { Delivered = delivered, DeliveredOrder = order };
        });
    }

    private static string CreateDiagnosticKey(LspDiagnosticItem diag) {
        return $"{diag.Message}|{diag.Severity}|{diag.Range?.Start.Line}:{diag.Range?.Start.Character}-{diag.Range?.End.Line}:{diag.Range?.End.Character}|{diag.Source}|{diag.Code}";
    }

    private static int SeverityToNumber(string? severity) => severity switch {
        "Error" => 1,
        "Warning" => 2,
        "Info" => 3,
        "Hint" => 4,
        _ => 4
    };

    /// <inheritdoc />
    public override void Dispose() {
        base.Dispose();
    }
}