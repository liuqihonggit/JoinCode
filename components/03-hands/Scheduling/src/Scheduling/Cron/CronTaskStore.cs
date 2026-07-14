
namespace Core.Scheduling.Cron;

using JoinCode.Abstractions.Attributes;

/// <summary>
/// 文件存储的 Cron 任务存储实现
/// 锁内只做内存操作和JSON序列化（快），文件I/O（慢）在锁外执行
/// </summary>
[Register]
public sealed class FileCronTaskStore : ICronTaskStore, IDisposable
{
    private readonly string _filePath;
    private readonly IFileOperationService _fileOperationService;
    private readonly IFileSystem _fs;
    private readonly IClockService _clock;
    private readonly SemaphoreSlim _semaphore;
    private readonly List<CronTask> _sessionTasks = new();
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

        _filePath = Path.Combine(dir, AppDataConstants.ScheduledTasksFileName);
        _fileOperationService = fileOperationService ?? throw new ArgumentNullException(nameof(fileOperationService));
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _clock = clock ?? SystemClockService.Instance;
        _semaphore = new SemaphoreSlim(1, 1);
        Diag.WriteLine("[DI] FileCronTaskStore.ctor calling InitializeWatcher...");
        InitializeWatcher();
        Diag.WriteLine("[DI] FileCronTaskStore.ctor done");
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
            allTasks.AddRange(_sessionTasks);
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
                _sessionTasks.Add(task);
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

            await WriteJsonAsync(jsonToWrite!, cancellationToken).ConfigureAwait(false);
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
            _sessionTasks.RemoveAll(t => idSet.Contains(t.Id));

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
            foreach (var task in _sessionTasks.Where(t => idSet.Contains(t.Id)))
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

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        _watcher?.Dispose();
        _semaphore.Dispose();
    }
}
