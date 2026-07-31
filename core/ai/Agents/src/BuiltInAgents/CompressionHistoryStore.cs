namespace Core.Agents;

public sealed class CompressionHistoryStore : IDisposable
{
    private readonly List<CompressionReport> _history = new();
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly int _maxSize;

    public CompressionHistoryStore(int maxSize = 100)
    {
        _maxSize = maxSize;
    }

    public async Task AddAsync(CompressionReport report, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _history.Add(report);
            if (_history.Count > _maxSize)
            {
                _history.RemoveAt(0);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyList<CompressionReport>> GetAllAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return _history;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<CompressionReport?> FindByIdAsync(string reportId, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return _history.FirstOrDefault(r => r.ReportId == reportId);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyList<CompressionReport>> GetRecentAsync(int count = 10, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return _history
                .OrderByDescending(r => r.Timestamp)
                .Take(count)
                .ToList();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task ClearAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _history.Clear();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<Dictionary<string, JsonElement>> GetStatisticsAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_history.Count == 0)
            {
                return new Dictionary<string, JsonElement>
                {
                    ["TotalOperations"] = JsonSerializer.SerializeToElement(0, AgentsJsonContext.Default.Int32),
                    ["AverageCompressionRatio"] = JsonSerializer.SerializeToElement(0.0, AgentsJsonContext.Default.Double),
                    ["TotalTokensSaved"] = JsonSerializer.SerializeToElement(0, AgentsJsonContext.Default.Int32)
                };
            }

            var successfulReports = _history.Where(r => r.IsSuccess && r.CompressionRatio > 0).ToList();
            var averageRatio = successfulReports.Any()
                ? successfulReports.Average(r => r.CompressionRatio)
                : 0.0;

            var totalTokensSaved = successfulReports.Sum(r => r.OriginalTokenCount - r.CompressedTokenCount);

            return new Dictionary<string, JsonElement>
            {
                ["TotalOperations"] = JsonSerializer.SerializeToElement(_history.Count, AgentsJsonContext.Default.Int32),
                ["SuccessfulOperations"] = JsonSerializer.SerializeToElement(successfulReports.Count, AgentsJsonContext.Default.Int32),
                ["FailedOperations"] = JsonSerializer.SerializeToElement(_history.Count(r => !r.IsSuccess), AgentsJsonContext.Default.Int32),
                ["AverageCompressionRatio"] = JsonSerializer.SerializeToElement(averageRatio, AgentsJsonContext.Default.Double),
                ["TotalTokensSaved"] = JsonSerializer.SerializeToElement(totalTokensSaved, AgentsJsonContext.Default.Int32),
                ["LastOperationTime"] = JsonSerializer.SerializeToElement((_history.LastOrDefault()?.Timestamp ?? DateTime.MinValue).ToString("O"), AgentsJsonContext.Default.String)
            };
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose() => _lock.Dispose();
}
