
namespace Core.Memdir;

/// <summary>
/// 日志条目分类枚举
/// 定义助手日志条目的四种分类
/// </summary>
public enum DailyLogCategory
{
    /// <summary>
    /// 动作 - 助手执行的操作
    /// </summary>
    [EnumValue("action")]
    Action,

    /// <summary>
    /// 观察 - 助手观察到的信息
    /// </summary>
    [EnumValue("observation")]
    Observation,

    /// <summary>
    /// 决策 - 助手做出的决策
    /// </summary>
    [EnumValue("decision")]
    Decision,

    /// <summary>
    /// 结果 - 操作产生的结果
    /// </summary>
    [EnumValue("result")]
    Result
}

/// <summary>
/// 日志条目分类扩展方法
/// </summary>
public static class DailyLogCategoryExtensions
{
    private static readonly FrozenDictionary<string, DailyLogCategory> __reverseMap = new Dictionary<string, DailyLogCategory>
    {
        ["action"] = DailyLogCategory.Action,
        ["observation"] = DailyLogCategory.Observation,
        ["decision"] = DailyLogCategory.Decision,
        ["result"] = DailyLogCategory.Result
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<DailyLogCategory, string> CategoryLabels =
        new Dictionary<DailyLogCategory, string>
        {
            [DailyLogCategory.Action] = "动作",
            [DailyLogCategory.Observation] = "观察",
            [DailyLogCategory.Decision] = "决策",
            [DailyLogCategory.Result] = "结果"
        }.ToFrozenDictionary();

    /// <summary>
    /// 从字符串值解析枚举成员
    /// </summary>
    public static DailyLogCategory? FromValue(string? value)
        => value is not null && __reverseMap.TryGetValue(value, out var result) ? result : null;

    /// <summary>
    /// 获取分类的显示标签
    /// </summary>
    public static string GetLabel(this DailyLogCategory category)
    {
        return CategoryLabels.GetValueOrDefault(category, category.ToString());
    }
}

/// <summary>
/// 日志条目模型
/// 描述助手日志中的单条记录
/// </summary>
public sealed record DailyLogEntry
{
    /// <summary>
    /// 条目时间戳
    /// </summary>
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// 日志内容
    /// </summary>
    [JsonPropertyName("content")]
    public required string Content { get; init; }

    /// <summary>
    /// 日志分类
    /// </summary>
    [JsonPropertyName("category")]
    public DailyLogCategory Category { get; init; } = DailyLogCategory.Action;

    /// <summary>
    /// 关联的记忆 ID（可选）
    /// </summary>
    [JsonPropertyName("relatedMemoryId")]
    public string? RelatedMemoryId { get; init; }
}

/// <summary>
/// 日志文件模型
/// 描述一天的完整日志
/// </summary>
public sealed record DailyLogFile
{
    /// <summary>
    /// 日志日期（格式: yyyy-MM-dd）
    /// </summary>
    [JsonPropertyName("date")]
    public required string Date { get; init; }

    /// <summary>
    /// 日志条目列表
    /// </summary>
    [JsonPropertyName("entries")]
    public ImmutableList<DailyLogEntry> Entries { get; init; } = ImmutableList<DailyLogEntry>.Empty;
}

/// <summary>
/// 助手日志服务接口
/// 管理助手每日日志的追加式记录与查询
/// </summary>
public interface IAssistantDailyLogService : IDisposable
{
    /// <summary>
    /// 追加一条日志到今日日志
    /// </summary>
    /// <param name="content">日志内容</param>
    /// <param name="category">日志分类</param>
    /// <param name="relatedMemoryId">关联记忆 ID（可选）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>追加的日志条目</returns>
    Task<DailyLogEntry> AppendEntryAsync(
        string content,
        DailyLogCategory category = DailyLogCategory.Action,
        string? relatedMemoryId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取今日日志
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>今日日志文件</returns>
    Task<DailyLogFile> GetDailyLogAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取指定日期的日志
    /// </summary>
    /// <param name="date">日期</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>指定日期的日志文件</returns>
    Task<DailyLogFile> GetDailyLogForDateAsync(DateTime date, CancellationToken cancellationToken = default);

    /// <summary>
    /// 构建日志上下文提示文本
    /// </summary>
    /// <param name="maxEntries">最大条目数</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>格式化的日志提示文本</returns>
    Task<string> BuildDailyLogPromptAsync(int maxEntries = 20, CancellationToken cancellationToken = default);
}

/// <summary>
/// 助手日志服务实现
/// 以追加式写入方式管理每日日志，每天一个文件，
/// 存储在用户记忆目录下的 daily-logs 子目录中
/// </summary>
[Register]
public sealed partial class AssistantDailyLogService : IAssistantDailyLogService, IDisposable
{
    private const string DailyLogsDirectoryName = "daily-logs";
    private const string DateFormat = "yyyy-MM-dd";

    private readonly MemoryStore _memoryStore;
    private readonly IMemoryPaths _memoryPaths;
    private readonly IFileOperationService _fileOperationService;
    [Inject] private readonly ILogger<AssistantDailyLogService>? _logger;
    [Inject] private readonly IClockService _clock;
    private readonly SemaphoreSlim _writeLock;

    public AssistantDailyLogService(
        MemoryStore memoryStore,
        IMemoryPaths memoryPaths,
        IFileOperationService fileOperationService,
        ILogger<AssistantDailyLogService>? logger = null,
        IClockService? clock = null)
    {
        _memoryStore = memoryStore ?? throw new ArgumentNullException(nameof(memoryStore));
        _memoryPaths = memoryPaths ?? throw new ArgumentNullException(nameof(memoryPaths));
        _fileOperationService = fileOperationService ?? throw new ArgumentNullException(nameof(fileOperationService));
        _logger = logger;
        _clock = clock ?? SystemClockService.Instance;
        _writeLock = new SemaphoreSlim(1, 1);
    }

    /// <inheritdoc />
    public async Task<DailyLogEntry> AppendEntryAsync(
        string content,
        DailyLogCategory category = DailyLogCategory.Action,
        string? relatedMemoryId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        cancellationToken.ThrowIfCancellationRequested();
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var entry = new DailyLogEntry
            {
                Timestamp = _clock.GetUtcNow(),
                Content = content,
                Category = category,
                RelatedMemoryId = relatedMemoryId
            };

            var logFile = await LoadDailyLogCoreAsync(_clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);

            var updatedLog = logFile with
            {
                Entries = logFile.Entries.Add(entry)
            };

            await SaveDailyLogCoreAsync(updatedLog, cancellationToken).ConfigureAwait(false);

            _logger?.LogDebug(
                "[DailyLog] 追加日志条目: [{Category}] {Content}",
                category.GetLabel(),
                content[..Math.Min(50, content.Length)]);

            return entry;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<DailyLogFile> GetDailyLogAsync(CancellationToken cancellationToken = default)
    {
        return await LoadDailyLogCoreAsync(_clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<DailyLogFile> GetDailyLogForDateAsync(DateTime date, CancellationToken cancellationToken = default)
    {
        return await LoadDailyLogCoreAsync(date, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<string> BuildDailyLogPromptAsync(int maxEntries = 20, CancellationToken cancellationToken = default)
    {
        var logFile = await GetDailyLogAsync(cancellationToken).ConfigureAwait(false);

        if (logFile.Entries.IsEmpty)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"## 今日日志 ({logFile.Date})");
        sb.AppendLine();

        var entries = logFile.Entries
            .OrderByDescending(e => e.Timestamp)
            .Take(maxEntries);

        foreach (var entry in entries)
        {
            var timeStr = entry.Timestamp.ToString("HH:mm:ss");
            var categoryLabel = entry.Category.GetLabel();
            sb.AppendLine($"- [{timeStr}] [{categoryLabel}] {entry.Content}");
        }

        // 补充关联记忆的上下文
        var relatedMemoryIds = logFile.Entries
            .Where(e => !string.IsNullOrEmpty(e.RelatedMemoryId))
            .Select(e => e.RelatedMemoryId!)
            .Distinct()
            .Take(5)
            .ToList();

        if (relatedMemoryIds.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("### 关联记忆");

            foreach (var memoryId in relatedMemoryIds)
            {
                var memory = _memoryStore.GetMemory(memoryId);
                if (memory != null)
                {
                    var contentPreview = memory.Content[..Math.Min(80, memory.Content.Length)];
                    sb.AppendLine($"- [{memory.Type}] {contentPreview}");
                }
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// 获取日志文件路径
    /// </summary>
    private string GetDailyLogFilePath(DateTime date)
    {
        var userDir = _memoryPaths.GetUserMemoryDirectory();
        var dailyLogsDir = Path.Combine(userDir, DailyLogsDirectoryName);

        if (!_fileOperationService.DirectoryExists(dailyLogsDir))
        {
            _fileOperationService.CreateDirectory(dailyLogsDir);
        }

        return Path.Combine(dailyLogsDir, $"{date.ToString(DateFormat)}.json");
    }

    /// <summary>
    /// 加载日志文件（核心实现）
    /// </summary>
    private async Task<DailyLogFile> LoadDailyLogCoreAsync(DateTime date, CancellationToken cancellationToken)
    {
        var filePath = GetDailyLogFilePath(date);
        var dateStr = date.ToString(DateFormat);

        if (!_fileOperationService.FileExists(filePath))
        {
            return new DailyLogFile { Date = dateStr };
        }

        try
        {
            var result = await _fileOperationService.ReadFileAsync(filePath, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!result.Success)
            {
                _logger?.LogWarning("[DailyLog] 读取日志文件失败: {Path}", filePath);
                return new DailyLogFile { Date = dateStr };
            }

            var logFile = JsonSerializer.Deserialize(result.Content, DailyLogJsonContext.Default.DailyLogFile);
            return logFile ?? new DailyLogFile { Date = dateStr };
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[DailyLog] 解析日志文件异常: {Path}", filePath);
            return new DailyLogFile { Date = dateStr };
        }
    }

    /// <summary>
    /// 保存日志文件（核心实现）
    /// </summary>
    private async Task SaveDailyLogCoreAsync(DailyLogFile logFile, CancellationToken cancellationToken)
    {
        var date = DateTime.ParseExact(logFile.Date, DateFormat, null);
        var filePath = GetDailyLogFilePath(date);

        try
        {
            var json = JsonSerializer.Serialize(logFile, DailyLogJsonContext.Default.DailyLogFile);
            var result = await _fileOperationService.WriteFileAsync(filePath, json, cancellationToken).ConfigureAwait(false);

            if (!result.Success)
            {
                _logger?.LogError("[DailyLog] 保存日志文件失败: {Path}, 错误: {Error}", filePath, result.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[DailyLog] 保存日志文件异常: {Path}", filePath);
        }
    }

    public void Dispose() => _writeLock.Dispose();
}

