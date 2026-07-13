namespace JoinCode.CodeIndex;

/// <summary>
/// 增量更新器 — 重写为基于 InMemoryIndexStore 的内存操作
/// 不再使用 SQLite 事务,所有数据从 store.FileTracking 读取
/// </summary>
public sealed class IncrementalUpdater : IDisposable
{
    /// <summary>
    /// 强制排除的目录名(对齐全量扫描 CodeIndexer 与 FileWatcher 的排除规则)
    /// </summary>
    private static readonly HashSet<string> ExcludedDirs = new(StringComparer.OrdinalIgnoreCase) { "bin", "obj", ".git", ".x" };

    private readonly SymbolIndex _index;
    private readonly InMemoryIndexStore _store;
    private readonly IFileSystem _fs;
    private readonly Func<ILanguagePlugin> _pluginFactory;
    private int _disposed;

    /// <summary>
    /// 检查路径中是否包含被排除的目录段(bin/obj/.git/.x)
    /// </summary>
    private static bool IsInExcludedDirectory(string filePath)
    {
        var span = filePath.AsSpan();
        while (!span.IsEmpty)
        {
            var idx = span.IndexOfAny(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var segment = idx < 0 ? span : span[..idx];
            if (!segment.IsEmpty && ExcludedDirs.Contains(segment.ToString()))
            {
                return true;
            }
            span = idx < 0 ? [] : span[(idx + 1)..];
        }
        return false;
    }

    public IncrementalUpdater(SymbolIndex index, InMemoryIndexStore store, IFileSystem fs, Func<ILanguagePlugin> pluginFactory)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(fs);
        ArgumentNullException.ThrowIfNull(pluginFactory);

        _index = index;
        _store = store;
        _fs = fs;
        _pluginFactory = pluginFactory;
    }

    public async Task<IncrementalUpdateResult> UpdateAsync(string filePath, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        ObjectDisposedException.ThrowIf(_disposed != 0, this);

        if (!_fs.FileExists(filePath))
        {
            var wasTracked = IsFileTracked(filePath);
            if (wasTracked)
            {
                await _index.RemoveFileAsync(filePath, ct).ConfigureAwait(false);
                return new IncrementalUpdateResult { WasUpdated = true };
            }

            return new IncrementalUpdateResult { WasUpdated = false };
        }

        var (sourceCode, currentHash) = await HashUtility.ReadFileAndComputeHashAsync(filePath, _fs, ct).ConfigureAwait(false);

        var storedHash = GetStoredHash(filePath);
        if (storedHash is not null && storedHash == currentHash)
        {
            return new IncrementalUpdateResult { WasUpdated = false };
        }

        var extraction = _pluginFactory().ExtractAll(sourceCode, filePath);
        await _index.IndexFileWithContentAsync(filePath, sourceCode, currentHash, extraction, ct).ConfigureAwait(false);

        return new IncrementalUpdateResult { WasUpdated = true };
    }

    public async Task<DirectoryUpdateResult> UpdateDirectoryAsync(string directoryPath, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(directoryPath);
        ObjectDisposedException.ThrowIf(_disposed != 0, this);

        // 过滤掉 bin/obj/.git/.x 目录下的文件(对齐全量扫描与 FileWatcher 排除规则)
        var csFiles = _fs.GetFiles(directoryPath, "*.cs", SearchOption.AllDirectories)
            .Where(f => !IsInExcludedDirectory(f))
            .ToArray();
        var storedHashes = BatchGetStoredHashes(csFiles);
        var trackedFiles = GetTrackedFilesInDirectory(directoryPath);

        var updatedCount = 0;
        var skippedCount = 0;
        var deletedCount = 0;

        var existingFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var readTasks = csFiles.Select(async filePath =>
        {
            var (sourceCode, currentHash) = await HashUtility.ReadFileAndComputeHashAsync(filePath, _fs, ct).ConfigureAwait(false);
            var needsUpdate = !storedHashes.TryGetValue(filePath, out var storedHash) || storedHash != currentHash;
            return (FilePath: filePath, SourceCode: sourceCode, Hash: currentHash, NeedsUpdate: needsUpdate);
        }).ToArray();

        var readResults = await Task.WhenAll(readTasks).ConfigureAwait(false);

        var filesToIndex = new List<(string FilePath, string SourceCode, string Hash)>();
        foreach (var r in readResults)
        {
            existingFiles.Add(r.FilePath);
            if (r.NeedsUpdate)
            {
                filesToIndex.Add((r.FilePath, r.SourceCode, r.Hash));
            }
            else
            {
                skippedCount++;
            }
        }

        var extractionResults = ParallelExtractAll(filesToIndex, ct);

        for (var i = 0; i < extractionResults.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var (filePath, sourceCode, hash) = filesToIndex[i];
            var extraction = extractionResults[i];
            await _index.IndexFileWithContentAsync(filePath, sourceCode, hash, extraction, ct).ConfigureAwait(false);
            updatedCount++;
        }

        foreach (var trackedFile in trackedFiles)
        {
            if (!existingFiles.Contains(trackedFile))
            {
                await _index.RemoveFileAsync(trackedFile, ct).ConfigureAwait(false);
                deletedCount++;
            }
        }

        return new DirectoryUpdateResult
        {
            UpdatedCount = updatedCount,
            SkippedCount = skippedCount,
            DeletedCount = deletedCount
        };
    }

    private List<ExtractionResult> ParallelExtractAll(
        List<(string FilePath, string SourceCode, string Hash)> files, CancellationToken ct)
    {
        if (files.Count == 0) return [];

        var parallelism = Math.Min(4, CpuParallelism.GetDegree());
        var results = new ExtractionResult[files.Count];

        var chunkSize = Math.Max(1, (files.Count + parallelism - 1) / parallelism);
        var chunks = new List<(int Start, int End)>();
        for (var i = 0; i < files.Count; i += chunkSize)
        {
            chunks.Add((i, Math.Min(i + chunkSize, files.Count)));
        }

        chunks
            .AsParallel()
            .WithDegreeOfParallelism(parallelism)
            .WithCancellation(ct)
            .ForAll(chunk =>
            {
                using var parser = TreeSitterParserPool.CreateDisposable();
                using var extractor = new CSharpSymbolExtractor(parser);

                for (var i = chunk.Start; i < chunk.End; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var f = files[i];
                    results[i] = extractor.ExtractAll(f.SourceCode, f.FilePath);
                }
            });

        return [.. results];
    }

    private bool IsFileTracked(string filePath)
    {
        using var scope = _store.EnterReadLock();
        return _store.FileTracking.ContainsKey(filePath);
    }

    private string? GetStoredHash(string filePath)
    {
        using var scope = _store.EnterReadLock();
        return _store.FileTracking.TryGetValue(filePath, out var entry) ? entry.Hash : null;
    }

    private Dictionary<string, string> BatchGetStoredHashes(string[] filePaths)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (filePaths.Length == 0) return result;

        using var scope = _store.EnterReadLock();
        foreach (var fp in filePaths)
        {
            if (_store.FileTracking.TryGetValue(fp, out var entry))
            {
                result[fp] = entry.Hash;
            }
        }
        return result;
    }

    private IReadOnlyList<string> GetTrackedFilesInDirectory(string directoryPath)
    {
        using var scope = _store.EnterReadLock();
        return _store.FileTracking.Keys
            .Where(p => p.StartsWith(directoryPath, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }
    }
}
