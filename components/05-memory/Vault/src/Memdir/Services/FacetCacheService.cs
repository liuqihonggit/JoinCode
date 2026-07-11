namespace Memdir.Services;

/// <summary>
/// Facet 缓存服务 — 对齐 TS insights.ts loadCachedFacets + saveFacets
/// 缓存路径: ~/.jcc/usage-data/facets/{sessionId}.json
/// </summary>
[Register]
public sealed partial class FacetCacheService : IFacetCacheService
{
    private readonly string _facetsDirectory;
    [Inject] private readonly ILogger<FacetCacheService>? _logger;
    private readonly IFileSystem _fs;

    public FacetCacheService(IFileSystem fs, string? facetsDirectory = null, ILogger<FacetCacheService>? logger = null)
    {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _facetsDirectory = facetsDirectory
            ?? Path.Combine(
                WorkflowConstants.Paths.JccDirectory,
                "usage-data",
                "facets");
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<SessionFacets?> LoadAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var filePath = GetFacetFilePath(sessionId);
        if (!_fs.FileExists(filePath))
        {
            return null;
        }

        try
        {
            var json = await _fs.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
            var facets = JsonSerializer.Deserialize(json, SessionFacetsJsonContext.Default.SessionFacets);

            if (facets is not null && IsValidFacets(facets))
            {
                return facets;
            }

            // 损坏的缓存 — 对齐 TS: 校验失败时删除缓存文件
            _logger?.LogWarning("Facet 缓存校验失败，删除: {SessionId}", sessionId);
            MoveToDeleted(_fs, filePath);

            return null;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "读取 Facet 缓存失败: {SessionId}", sessionId);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task SaveAsync(SessionFacets facets, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(facets);

        try
        {
            _fs.CreateDirectory(_facetsDirectory);

            var filePath = GetFacetFilePath(facets.SessionId);
            var json = JsonSerializer.Serialize(facets, SessionFacetsJsonContext.Default.SessionFacets);

            await _fs.WriteAllTextAsync(filePath, json, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "保存 Facet 缓存失败: {SessionId}", facets.SessionId);
        }
    }

    /// <inheritdoc />
    public async Task<bool> IsValidAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var facets = await LoadAsync(sessionId, cancellationToken).ConfigureAwait(false);
        return facets is not null;
    }

    /// <summary>
    /// 校验 Facet 必要字段 — 对齐 TS isValidSessionFacets
    /// </summary>
    private static bool IsValidFacets(SessionFacets facets)
    {
        return !string.IsNullOrEmpty(facets.UnderlyingGoal)
            && !string.IsNullOrEmpty(facets.Outcome)
            && !string.IsNullOrEmpty(facets.BriefSummary)
            && facets.GoalCategories.Count > 0
            && facets.UserSatisfactionCounts.Count > 0
            && facets.FrictionCounts.Count >= 0; // friction 可以为空
    }

    private string GetFacetFilePath(string sessionId)
    {
        // 清理 sessionId 中的路径分隔符
        var safeName = sessionId.Replace('/', '_').Replace('\\', '_');
        return Path.Combine(_facetsDirectory, $"{safeName}.json");
    }

    /// <summary>
    /// 移动损坏缓存到 .x/ 目录 — 遵循项目安全删除规则
    /// </summary>
    private static void MoveToDeleted(IFileSystem fs, string filePath)
    {
        try
        {
            var dir = Path.GetDirectoryName(filePath) ?? ".";
            var xDir = Path.Combine(dir, ".x");
            fs.CreateDirectory(xDir);

            var ts = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var fileName = Path.GetFileName(filePath);
            var destPath = Path.Combine(xDir, $"{fileName}.{ts}.del");

            if (fs.FileExists(filePath))
            {
                fs.MoveFile(filePath, destPath);
            }
        }
        catch (Exception ex)
        {
            // 移动失败不影响主流程
            System.Diagnostics.Trace.WriteLine($"FacetCacheService: Failed to move corrupted cache file to .x directory: {ex.Message}");
        }
    }
}
