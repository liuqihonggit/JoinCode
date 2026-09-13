namespace Services.Lsp.Internal;

/// <summary>
/// LSP 诊断项 — 单条诊断消息（含严重级别、范围、来源、代码）
/// </summary>
public sealed class LspDiagnosticItem
{
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
public sealed class LspDiagnosticFile
{
    /// <summary>文件 URI</summary>
    public required string Uri { get; init; }
    /// <summary>该文件的诊断列表</summary>
    public required List<LspDiagnosticItem> Diagnostics { get; set; } = [];
}

/// <summary>
/// LSP 待处理诊断 — 来自某服务器的待发送诊断批次
/// </summary>
public sealed class LspPendingDiagnostic
{
    /// <summary>服务器名称</summary>
    public required string ServerName { get; init; }
    /// <summary>待发送的文件诊断列表</summary>
    public required List<LspDiagnosticFile> Files { get; init; }
    /// <summary>时间戳（毫秒）</summary>
    public long Timestamp { get; init; }
    /// <summary>是否已作为附件发送</summary>
    public bool AttachmentSent { get; set; }
}

/// <summary>
/// LSP 诊断注册表接口 — 管理待处理诊断的注册、检查、清理与重置
/// </summary>
public interface ILspDiagnosticRegistry : IRegistry
{
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
public sealed partial class LspDiagnosticRegistry : ServiceEntity, ILspDiagnosticRegistry, JoinCode.Abstractions.Interfaces.Lsp.ILspDiagnosticProvider
{

    /// <summary>
    /// 构造函数 — 注入时钟服务
    /// </summary>
    /// <param name="clock">时钟服务</param>
    public LspDiagnosticRegistry(IClockService clock)
    {
        _clock = clock;
    }
    private const int MaxDiagnosticsPerFile = 10;
    private const int MaxTotalDiagnostics = 30;
    private const int MaxDeliveredFiles = 500;

    private readonly IClockService _clock;
    private readonly AsyncLock _lock = new("LspDiagnosticRegistry");
    private readonly Dictionary<string, LspPendingDiagnostic> _pending = new();
    private readonly LinkedList<string> _deliveredLru = new();
    private readonly Dictionary<string, HashSet<string>> _delivered = new();

    /// <summary>待处理诊断数量</summary>
    public int PendingCount
    {
        get
        {
            using (_lock.TryLock() ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时")) { return _pending.Count; }
        }
    }

    /// <summary>
    /// 注册待处理诊断
    /// </summary>
    /// <param name="serverName">服务器名称</param>
    /// <param name="files">文件诊断列表</param>
    public void RegisterPending(string serverName, List<LspDiagnosticFile> files)
    {
        if (files.Count == 0) return;

        var id = Guid.NewGuid().ToString("N");
        var diagnostic = new LspPendingDiagnostic
        {
            ServerName = serverName,
            Files = files,
            Timestamp = _clock.GetUtcNowOffset().ToUnixTimeMilliseconds(),
            AttachmentSent = false
        };

        using (_lock.TryLock() ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时"))
        {
            _pending[id] = diagnostic;
        }
    }

    /// <summary>
    /// 检查并取出待处理诊断 — 去重、容量限制、追踪已投递
    /// </summary>
    /// <returns>服务器名与文件诊断列表的元组列表</returns>
    public List<(string ServerName, List<LspDiagnosticFile> Files)> CheckPending()
    {
        List<LspDiagnosticFile> allFiles;
        HashSet<string> serverNames;
        List<LspPendingDiagnostic> toMark;

        using (_lock.TryLock() ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时"))
        {
            if (_pending.Count == 0) return [];

            allFiles = [];
            serverNames = new HashSet<string>(StringComparer.Ordinal);
            toMark = [];

            foreach (var diag in _pending.Values)
            {
                if (!diag.AttachmentSent)
                {
                    allFiles.AddRange(diag.Files);
                    serverNames.Add(diag.ServerName);
                    toMark.Add(diag);
                }
            }
        }

        if (allFiles.Count == 0) return [];

        var dedupedFiles = DeduplicateDiagnosticFiles(allFiles);

        foreach (var diag in toMark)
        {
            diag.AttachmentSent = true;
        }

        using (_lock.TryLock() ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时"))
        {
            var keysToRemove = _pending
                .Where(kvp => kvp.Value.AttachmentSent)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in keysToRemove)
            {
                _pending.Remove(key);
            }
        }

        ApplyVolumeLimits(dedupedFiles);

        TrackDelivered(dedupedFiles);

        if (dedupedFiles.Count == 0) return [];

        return
        [
            (string.Join(", ", serverNames), dedupedFiles)
        ];
    }

    /// <summary>清空所有待处理诊断</summary>
    public void ClearAll()
    {
        using (_lock.TryLock() ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时"))
        {
            _pending.Clear();
        }
    }

    /// <summary>重置所有状态（待处理与已投递）</summary>
    public void ResetAll()
    {
        using (_lock.TryLock() ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时"))
        {
            _pending.Clear();
            _delivered.Clear();
            _deliveredLru.Clear();
        }
    }

    /// <summary>
    /// 清除指定文件的已投递记录
    /// </summary>
    /// <param name="fileUri">文件 URI</param>
    public void ClearDeliveredForFile(string fileUri)
    {
        using (_lock.TryLock() ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时"))
        {
            if (_delivered.Remove(fileUri))
            {
                _deliveredLru.Remove(fileUri);
            }
        }
    }

    /// <summary>
    /// 检查并取出待处理诊断摘要 — 转换为 LspDiagnosticSummary 形式
    /// </summary>
    /// <returns>服务器名与诊断摘要列表的元组列表</returns>
    public List<(string ServerName, List<JoinCode.Abstractions.Interfaces.Lsp.LspDiagnosticSummary> Files)> CheckPendingDiagnostics()
    {
        var pending = CheckPending();
        var result = new List<(string ServerName, List<JoinCode.Abstractions.Interfaces.Lsp.LspDiagnosticSummary> Files)>();

        foreach (var (serverName, files) in pending)
        {
            var summaries = files.Select(f => new JoinCode.Abstractions.Interfaces.Lsp.LspDiagnosticSummary
            {
                Uri = f.Uri,
                Diagnostics = f.Diagnostics.Select(d => new JoinCode.Abstractions.Interfaces.Lsp.LspDiagnosticEntry
                {
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

    private List<LspDiagnosticFile> DeduplicateDiagnosticFiles(List<LspDiagnosticFile> allFiles)
    {
        var fileMap = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var dedupedFileMap = new Dictionary<string, LspDiagnosticFile>(StringComparer.OrdinalIgnoreCase);
        var dedupedFiles = new List<LspDiagnosticFile>();

        using (_lock.TryLock() ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时"))
        {
            foreach (var file in allFiles)
            {
                if (!fileMap.ContainsKey(file.Uri))
                {
                    fileMap[file.Uri] = new HashSet<string>(StringComparer.Ordinal);
                    var newDedupedFile = new LspDiagnosticFile { Uri = file.Uri, Diagnostics = [] };
                    dedupedFiles.Add(newDedupedFile);
                    dedupedFileMap[file.Uri] = newDedupedFile;
                }

                var seenDiagnostics = fileMap[file.Uri];
                var dedupedFile = dedupedFileMap[file.Uri];

                _delivered.TryGetValue(file.Uri, out var previouslyDelivered);
                previouslyDelivered ??= new HashSet<string>(StringComparer.Ordinal);

                foreach (var diag in file.Diagnostics)
                {
                    var key = CreateDiagnosticKey(diag);

                    if (seenDiagnostics.Contains(key) || previouslyDelivered.Contains(key))
                    {
                        continue;
                    }

                    seenDiagnostics.Add(key);
                    dedupedFile.Diagnostics.Add(diag);
                }
            }
        }

        return dedupedFiles.Where(f => f.Diagnostics.Count > 0).ToList();
    }

    private static void ApplyVolumeLimits(List<LspDiagnosticFile> files)
    {
        var totalDiagnostics = 0;
        var truncatedCount = 0;

        foreach (var file in files)
        {
            file.Diagnostics.Sort((a, b) => SeverityToNumber(a.Severity) - SeverityToNumber(b.Severity));

            if (file.Diagnostics.Count > MaxDiagnosticsPerFile)
            {
                truncatedCount += file.Diagnostics.Count - MaxDiagnosticsPerFile;
                file.Diagnostics = file.Diagnostics[..MaxDiagnosticsPerFile];
            }

            var remainingCapacity = MaxTotalDiagnostics - totalDiagnostics;
            if (file.Diagnostics.Count > remainingCapacity)
            {
                truncatedCount += file.Diagnostics.Count - remainingCapacity;
                file.Diagnostics = file.Diagnostics[..remainingCapacity];
            }

            totalDiagnostics += file.Diagnostics.Count;
        }

        files.RemoveAll(f => f.Diagnostics.Count == 0);
    }

    private void TrackDelivered(List<LspDiagnosticFile> files)
    {
        using (_lock.TryLock() ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时"))
        {
            foreach (var file in files)
            {
                if (!_delivered.ContainsKey(file.Uri))
                {
                    _delivered[file.Uri] = new HashSet<string>(StringComparer.Ordinal);
                    _deliveredLru.AddLast(file.Uri);
                }

                foreach (var diag in file.Diagnostics)
                {
                    _delivered[file.Uri].Add(CreateDiagnosticKey(diag));
                }

                while (_delivered.Count > MaxDeliveredFiles)
                {
                    var first = _deliveredLru.First ?? throw new InvalidOperationException("LRU first node is null despite exceeding max count.");
                    var oldest = first.Value;
                    _deliveredLru.RemoveFirst();
                    _delivered.Remove(oldest);
                }
            }
        }
    }

    private static string CreateDiagnosticKey(LspDiagnosticItem diag)
    {
        return $"{diag.Message}|{diag.Severity}|{diag.Range?.Start.Line}:{diag.Range?.Start.Character}-{diag.Range?.End.Line}:{diag.Range?.End.Character}|{diag.Source}|{diag.Code}";
    }

    private static int SeverityToNumber(string? severity) => severity switch
    {
        "Error" => 1,
        "Warning" => 2,
        "Info" => 3,
        "Hint" => 4,
        _ => 4
    };
}
