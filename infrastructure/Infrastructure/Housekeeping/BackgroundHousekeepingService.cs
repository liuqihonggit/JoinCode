namespace Infrastructure.Housekeeping;

/// <summary>
/// 后台家政清理调度服务 — 对齐 TS startBackgroundHousekeeping
/// 启动后延迟 10 分钟执行首次清理，之后每 24 小时循环执行
/// 使用标记文件节流，24 小时内不重复执行
/// </summary>
public sealed class BackgroundHousekeepingService : PeriodicBackgroundServiceBase
{
    private readonly IHousekeepingService _housekeeping;
    private readonly IFileSystem _fs;
    private readonly IClockService _clock;
    private readonly ILogger<BackgroundHousekeepingService>? _logger;

    protected override TimeSpan InitialDelay => TimeSpan.FromMinutes(10);
    protected override TimeSpan Interval => TimeSpan.FromHours(24);
    protected override IClockService Clock => _clock;
    protected override ILogger? Logger => _logger;
    protected override string ServiceName => "后台家政清理服务";

    private static readonly string JccDir = WorkflowConstants.Paths.JccDirectory;
    private static readonly string MarkerFilePath = Path.Combine(
        WorkflowConstants.Paths.JccDirectory, ".housekeeping-last-run");

    private static readonly TimeSpan MarkerValidity = TimeSpan.FromHours(24);

    public BackgroundHousekeepingService(
        IHousekeepingService housekeeping,
        IFileSystem fs,
        IClockService clock,
        ILogger<BackgroundHousekeepingService>? logger = null)
    {
        _housekeeping = housekeeping;
        _fs = fs;
        _clock = clock;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        if (IsRecentlyRun()) return;

        try
        {
            var count = await _housekeeping.RunAllCleanupAsync(currentSessionId: "", cancellationToken).ConfigureAwait(false);
            WriteMarkerFile();

            if (count > 0)
            {
                _logger?.LogDebug("后台家政清理执行完成，清理 {Count} 项", count);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogDebug(ex, "后台家政清理执行失败");
        }
    }

    private bool IsRecentlyRun()
    {
        try
        {
            if (!_fs.FileExists(MarkerFilePath)) return false;

            var lastRun = _fs.GetLastWriteTimeUtc(MarkerFilePath);
            return _clock.GetUtcNow() - lastRun < MarkerValidity;
        }
        catch
        {
            return false;
        }
    }

    private void WriteMarkerFile()
    {
        try
        {
            if (!_fs.DirectoryExists(JccDir))
            {
                _fs.CreateDirectory(JccDir);
            }

            _fs.WriteAllText(MarkerFilePath, _clock.GetUtcNow().ToString("O"));
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "写入家政清理标记文件失败");
        }
    }
}
