namespace Services.Build;

internal sealed class BuildResultBuffer
{
    private readonly ConcurrentDictionary<string, BuildBufferEntry> _buffer = new();
    private readonly SourceFingerprintCache _fingerprintCache;
    private readonly ILogger? _logger;

    internal BuildResultBuffer(SourceFingerprintCache fingerprintCache, ILogger? logger)
    {
        _fingerprintCache = fingerprintCache;
        _logger = logger;
    }

    internal static string BuildBufferKey(string command, string? workingDirectory)
    {
        return $"{workingDirectory ?? ""}|{command}";
    }

    internal bool TryGet(string bufferKey, [NotNullWhen(true)] out BuildQueueResult? result)
    {
        result = null;

        if (!_buffer.TryGetValue(bufferKey, out var bufferEntry))
            return false;

        var currentFingerprint = _fingerprintCache.Compute(bufferEntry.WorkingDirectory);
        if (currentFingerprint != 0 && bufferEntry.SourceFingerprint != 0 && currentFingerprint != bufferEntry.SourceFingerprint)
        {
            _buffer.TryRemove(bufferKey, out _);
            _logger?.LogDebug("Result buffer invalidated by source fingerprint: {BufferKey}", bufferKey);
            return false;
        }

        result = bufferEntry.Result;
        return true;
    }

    internal void Add(string bufferKey, BuildQueueResult result, string? workingDirectory)
    {
        var fingerprint = _fingerprintCache.Compute(workingDirectory);

        _buffer[bufferKey] = new BuildBufferEntry
        {
            Result = result,
            WorkingDirectory = workingDirectory,
            SourceFingerprint = fingerprint,
        };

        _logger?.LogInformation("Build result buffered: {BufferKey}", bufferKey);
    }

    internal void Clear()
    {
        _buffer.Clear();
        _logger?.LogInformation("Build result buffer cleared");
    }

    internal sealed class BuildBufferEntry
    {
        public required BuildQueueResult Result { get; init; }
        public string? WorkingDirectory { get; init; }
        public long SourceFingerprint { get; init; }
    }
}
