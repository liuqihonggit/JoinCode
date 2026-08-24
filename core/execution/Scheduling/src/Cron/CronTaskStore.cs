
namespace Core.Scheduling.Cron;

using JoinCode.Abstractions.Attributes;

/// <summary>
/// 文件存储的 Cron 任务存储实现
/// 锁内只做内存操作和JSON序列化（快），文件I/O（慢）在锁外执行
/// </summary>
[Register(typeof(ICronTaskStore), ServiceLifetime.Singleton)]
public sealed partial class FileCronTaskStore : ServiceEntity, ICronTaskStore, IDisposable
{
    private string _filePath;
    private readonly string _baseDir;
    private readonly IFileOperationService _fileOperationService;
    private readonly IFileSystem _fs;
    private readonly IClockService _clock;
    private readonly SemaphoreSlim _semaphore;
    private readonly Dictionary<string, CronTask> _sessionTasks = new();
    private IFileSystemWatcher? _watcher;
    private bool _disposed;



    public FileCronTaskStore(
        IFileOperationService fileOperationService,
        IFileSystem fs,
        string? directory = null,
        IClockService? clock = null)
    {
        Diag.WriteLine("[DI] FileCronTaskStore.ctor start");
        var dir = directory ?? Path.Combine(AppContext.BaseDirectory, "cron-tasks");
        if (string.IsNullOrWhiteSpace(dir))
            throw new ArgumentException("Directory cannot be null or empty", nameof(directory));

        _baseDir = dir;
        _filePath = Path.Combine(dir, AppDataConstants.ScheduledTasksFileName);
        _fileOperationService = fileOperationService ?? throw new ArgumentNullException(nameof(fileOperationService));
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _clock = clock ?? SystemClockService.Instance;
        _semaphore = new SemaphoreSlim(1, 1);
        Diag.WriteLine("[DI] FileCronTaskStore.ctor calling InitializeWatcher...");
        InitializeWatcher();
        Diag.WriteLine("[DI] FileCronTaskStore.ctor done");
    }

    /// <summary>
    /// 设置会话隔离标识 — 重新计算文件路径并重新初始化 watcher。
    /// 路径变为 {_baseDir}/{sessionId}/{ScheduledTasksFileName}。
    /// </summary>
    public void SetSessionId(string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        _watcher?.Dispose();
        _watcher = null;
        var sessionDir = Path.Combine(_baseDir, sessionId);
        _filePath = Path.Combine(sessionDir, AppDataConstants.ScheduledTasksFileName);
        InitializeWatcher();
    }

    private void InitializeWatcher()
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (string.IsNullOrEmpty(directory)) return;

        _fs.CreateDirectory(directory);

        _watcher = _fs.Watch(directory, Path.GetFileName(_filePath));
        _watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime;

        _watcher.Changed += OnFileChanged;
        _watcher.Created += OnFileChanged;
        _watcher.Deleted += OnFileDeleted;
        _watcher.EnableRaisingEvents = true;
    }

    private void OnFileChanged(object? sender, FileChangedEventArgs e)
    {
    }

    private void OnFileDeleted(object? sender, FileChangedEventArgs e)
    {
    }

    public async Task<IReadOnlyList<CronTask>> GetAllTasksAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var fileTasks = await ReadFileTasksAsync(cancellationToken).ConfigureAwait(false);
            var allTasks = new List<CronTask>(fileTasks);
            allTasks.AddRange(_sessionTasks.Values);
            return allTasks;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<CronTask> AddTaskAsync(CreateCronTaskRequest request, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!CronExpressionParser.IsValid(request.CronExpression))
            throw new ArgumentException("Invalid cron expression", nameof(request));

        var task = new CronTask
        {
            Id = GenerateTaskId(),
            CronExpression = request.CronExpression,
            Prompt = request.Prompt,
            CreatedAt = _clock.GetUtcNowOffset().ToUnixTimeMilliseconds(),
            IsRecurring = request.IsRecurring,
            IsDurable = request.IsDurable,
            AgentId = request.AgentId
        };

        if (!request.IsDurable)
        {
            await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                _sessionTasks[task.Id] = task;
            }
            finally
            {
                _semaphore.Release();
            }
        }
        else
        {
            string? jsonToWrite = null;

            await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var tasks = await ReadFileTasksAsync(cancellationToken).ConfigureAwait(false);
                var taskList = tasks.ToList();
                taskList.Add(task);
                jsonToWrite = SerializeTasks(taskList);
            }
            finally
            {
                _semaphore.Release();
            }

            await WriteJsonAsync(jsonToWrite ?? throw new InvalidOperationException("Failed to serialize task list."), cancellationToken).ConfigureAwait(false);
        }

        return task;
    }

    public async Task RemoveTasksAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var idSet = new HashSet<string>(ids);
        if (idSet.Count == 0) return;

        string? jsonToWrite = null;

        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var id in idSet)
            {
                _sessionTasks.Remove(id);
            }

            var fileTasks = await ReadFileTasksAsync(cancellationToken).ConfigureAwait(false);
            var originalCount = fileTasks.Count;
            var filteredTasks = fileTasks.Where(t => !idSet.Contains(t.Id)).ToList();

            if (filteredTasks.Count < originalCount)
            {
                jsonToWrite = SerializeTasks(filteredTasks);
            }
        }
        finally
        {
            _semaphore.Release();
        }

        if (jsonToWrite != null)
        {
            await WriteJsonAsync(jsonToWrite, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task MarkTasksFiredAsync(IEnumerable<string> ids, long firedAt, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var idSet = new HashSet<string>(ids);
        if (idSet.Count == 0) return;

        string? jsonToWrite = null;

        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var task in _sessionTasks.Values.Where(t => idSet.Contains(t.Id)))
            {
                task.LastFiredAt = firedAt;
            }

            var fileTasks = await ReadFileTasksAsync(cancellationToken).ConfigureAwait(false);
            var changed = false;

            foreach (var task in fileTasks.Where(t => idSet.Contains(t.Id)))
            {
                task.LastFiredAt = firedAt;
                changed = true;
            }

            if (changed)
            {
                jsonToWrite = SerializeTasks(fileTasks);
            }
        }
        finally
        {
            _semaphore.Release();
        }

        if (jsonToWrite != null)
        {
            await WriteJsonAsync(jsonToWrite, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<CronTask?> GetTaskByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_sessionTasks.TryGetValue(id, out var sessionTask))
            return sessionTask;

        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var fileTasks = await ReadFileTasksAsync(cancellationToken).ConfigureAwait(false);
            return fileTasks.FirstOrDefault(t => t.Id == id);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<IReadOnlyList<CronTask>> GetTasksByAgentIdAsync(string agentId, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var fileTasks = await ReadFileTasksAsync(cancellationToken).ConfigureAwait(false);
            var result = new List<CronTask>();

            foreach (var t in _sessionTasks.Values)
            {
                if (t.AgentId == agentId)
                    result.Add(t);
            }

            foreach (var t in fileTasks)
            {
                if (t.AgentId == agentId && !_sessionTasks.ContainsKey(t.Id))
                    result.Add(t);
            }

            return result;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task<IReadOnlyList<CronTask>> ReadFileTasksAsync(CancellationToken cancellationToken)
    {
        var result = await _fileOperationService.ReadFileAsync(_filePath, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!result.Success)
            return Array.Empty<CronTask>();

        try
        {
            var file = JsonSerializer.Deserialize(result.Content, SchedulingIndentedJsonContext.Default.CronTaskFile);

            if (file?.Tasks == null)
                return Array.Empty<CronTask>();

            var validTasks = file.Tasks
                .Where(task => ValidateTask(task) && CronExpressionParser.IsValid(task.CronExpression))
                .ToList();

            return validTasks;
        }
        catch (JsonException)
        {
            return Array.Empty<CronTask>();
        }
    }

    private static string SerializeTasks(IReadOnlyList<CronTask> tasks)
    {
        var file = new CronTaskFile { Tasks = tasks.ToList() };
        return JsonSerializer.Serialize(file, SchedulingIndentedJsonContext.Default.CronTaskFile);
    }

    private async Task WriteJsonAsync(string json, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            _fs.CreateDirectory(directory);
        }

        await _fileOperationService.WriteFileAsync(_filePath, json, cancellationToken).ConfigureAwait(false);
    }

    private static bool ValidateTask(CronTask task)
    {
        return !string.IsNullOrEmpty(task.Id)
            && !string.IsNullOrEmpty(task.CronExpression)
            && !string.IsNullOrEmpty(task.Prompt)
            && task.CreatedAt > 0;
    }

    private static string GenerateTaskId()
    {
        return Guid.NewGuid().ToString("N")[..8];
    }

    protected override void OnDispose()
    {
        if (_disposed) return;

        _disposed = true;
        _watcher?.Dispose();
        _semaphore.Dispose();
    }
}
