namespace Services.Lsp.Internal;

public sealed class LspDiagnosticItem
{
    public required string Message { get; init; }
    public string? Severity { get; init; }
    public LspRange? Range { get; init; }
    public string? Source { get; init; }
    public string? Code { get; init; }
}

public sealed class LspDiagnosticFile
{
    public required string Uri { get; init; }
    public required List<LspDiagnosticItem> Diagnostics { get; set; } = [];
}

public sealed class LspPendingDiagnostic
{
    public required string ServerName { get; init; }
    public required List<LspDiagnosticFile> Files { get; init; }
    public long Timestamp { get; init; }
    public bool AttachmentSent { get; set; }
}

public interface ILspDiagnosticRegistry
{
    void RegisterPending(string serverName, List<LspDiagnosticFile> files);
    List<(string ServerName, List<LspDiagnosticFile> Files)> CheckPending();
    void ClearAll();
    void ResetAll();
    void ClearDeliveredForFile(string fileUri);
    int PendingCount { get; }
}

[Register(typeof(ILspDiagnosticRegistry)), Register(typeof(JoinCode.Abstractions.Interfaces.Lsp.ILspDiagnosticProvider))]
public sealed partial class LspDiagnosticRegistry : ILspDiagnosticRegistry, JoinCode.Abstractions.Interfaces.Lsp.ILspDiagnosticProvider
{
    private const int MaxDiagnosticsPerFile = 10;
    private const int MaxTotalDiagnostics = 30;
    private const int MaxDeliveredFiles = 500;

    [Inject] private readonly IClockService _clock;
    private readonly object _lock = new();
    private readonly Dictionary<string, LspPendingDiagnostic> _pending = new();
    private readonly LinkedList<string> _deliveredLru = new();
    private readonly Dictionary<string, HashSet<string>> _delivered = new();

    public int PendingCount
    {
        get
        {
            lock (_lock) { return _pending.Count; }
        }
    }

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

        lock (_lock)
        {
            _pending[id] = diagnostic;
        }
    }

    public List<(string ServerName, List<LspDiagnosticFile> Files)> CheckPending()
    {
        List<LspDiagnosticFile> allFiles;
        HashSet<string> serverNames;
        List<LspPendingDiagnostic> toMark;

        lock (_lock)
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

        lock (_lock)
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

    public void ClearAll()
    {
        lock (_lock)
        {
            _pending.Clear();
        }
    }

    public void ResetAll()
    {
        lock (_lock)
        {
            _pending.Clear();
            _delivered.Clear();
            _deliveredLru.Clear();
        }
    }

    public void ClearDeliveredForFile(string fileUri)
    {
        lock (_lock)
        {
            if (_delivered.Remove(fileUri))
            {
                _deliveredLru.Remove(fileUri);
            }
        }
    }

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
        var dedupedFiles = new List<LspDiagnosticFile>();

        lock (_lock)
        {
            foreach (var file in allFiles)
            {
                if (!fileMap.ContainsKey(file.Uri))
                {
                    fileMap[file.Uri] = new HashSet<string>(StringComparer.Ordinal);
                    dedupedFiles.Add(new LspDiagnosticFile { Uri = file.Uri, Diagnostics = [] });
                }

                var seenDiagnostics = fileMap[file.Uri];
                var dedupedFile = dedupedFiles.First(f => f.Uri == file.Uri);

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
        lock (_lock)
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
