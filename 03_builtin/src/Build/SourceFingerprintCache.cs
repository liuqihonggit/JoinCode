namespace Services.Build;

internal sealed class SourceFingerprintCache
{
    private long _lastFingerprintTicks;
    private long _computedAtTicks;
    private readonly ILogger? _logger;

    private static readonly TimeSpan CacheValidity = TimeSpan.FromSeconds(1);

    internal SourceFingerprintCache(ILogger? logger)
    {
        _logger = logger;
    }

    internal long Compute(string? workingDirectory)
    {
        if (string.IsNullOrEmpty(workingDirectory)) return 0;

        var cachedTicks = Interlocked.Read(ref _lastFingerprintTicks);
        var computedAt = Interlocked.Read(ref _computedAtTicks);
        if (cachedTicks != 0 && computedAt != 0)
        {
            var computedAtTime = new DateTimeOffset(computedAt, TimeSpan.Zero);
            if (DateTimeOffset.UtcNow - computedAtTime < CacheValidity)
                return cachedTicks;
        }

        long maxTicks = 0;
        try
        {
            var dir = new DirectoryInfo(workingDirectory);
            if (!dir.Exists) return 0;

            foreach (var file in dir.EnumerateFiles("*.cs", SearchOption.AllDirectories))
            {
                if (file.LastWriteTimeUtc.Ticks > maxTicks)
                    maxTicks = file.LastWriteTimeUtc.Ticks;
            }
            foreach (var file in dir.EnumerateFiles("*.csproj", SearchOption.AllDirectories))
            {
                if (file.LastWriteTimeUtc.Ticks > maxTicks)
                    maxTicks = file.LastWriteTimeUtc.Ticks;
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger?.LogDebug(ex, "Access denied scanning source files in {Directory}", workingDirectory);
        }
        catch (DirectoryNotFoundException ex)
        {
            _logger?.LogDebug(ex, "Directory not found scanning source files: {Directory}", workingDirectory);
        }

        Interlocked.Exchange(ref _lastFingerprintTicks, maxTicks);
        Interlocked.Exchange(ref _computedAtTicks, DateTimeOffset.UtcNow.Ticks);

        return maxTicks;
    }
}
