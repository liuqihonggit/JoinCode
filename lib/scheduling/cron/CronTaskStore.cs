namespace Core.Scheduling.Cron;


/// <summary>
/// Cron 存储 Actor 命令 — Channel 中的消息类型
/// </summary>
public interface ICronStoreCommand;

internal sealed record GetAllTasksCmd(CancellationToken Ct, TaskCompletionSource<IReadOnlyList<CronTask>> Tcs) : ICronStoreCommand;
internal sealed record AddTaskCmd(CreateCronTaskRequest Request, CancellationToken Ct, TaskCompletionSource<CronTask> Tcs) : ICronStoreCommand;
internal sealed record RemoveTasksCmd(HashSet<string> Ids, CancellationToken Ct, TaskCompletionSource Tcs) : ICronStoreCommand;
internal sealed record MarkTasksFiredCmd(HashSet<string> Ids, long FiredAt, CancellationToken Ct, TaskCompletionSource Tcs) : ICronStoreCommand;
internal sealed record GetTaskByIdCmd(string Id, CancellationToken Ct, TaskCompletionSource<CronTask?> Tcs) : ICronStoreCommand;
internal sealed record GetTasksByAgentIdCmd(string AgentId, CancellationToken Ct, TaskCompletionSource<IReadOnlyList<CronTask>> Tcs) : ICronStoreCommand;

/// <summary>
/// 文件存储的 Cron 任务存储实现 — Actor 化：继承 ActorBase，Consumer 线程独占 _sessionTasks 和文件 I/O，
/// 消除 AsyncLock。文件读写由 Consumer 串行执行，不再阻塞其他 Cron 任务操作。
/// </summary>
[Register(typeof(ICronTaskStore), ServiceLifetime.Singleton)]
public sealed partial class FileCronTaskStore : ActorBase<ICronStoreCommand, Unit>, ICronTaskStore {
    private string _filePath;
    private readonly string _baseDir;
    private readonly IFileOperationService _fileOperationService;
    private readonly IFileSystem _fs;
    private readonly IClockService _clock;
    private readonly Dictionary<string, CronTask> _sessionTasks = new();
    private IFileSystemWatcher? _watcher;
    private volatile int _disposed;


    /// <summary>
    /// 初始化文件 Cron 任务存储
    /// </summary>
    /// <param name="fileOperationService">文件操作服务</param>
    /// <param name="fs">文件系统抽象</param>
    /// <param name="directory">任务文件存储目录，为 null 时使用应用数据目录下的 CronTasks 子目录</param>
    /// <param name="clock">时钟服务，为 null 时使用系统时钟</param>
    public FileCronTaskStore(
        IFileOperationService fileOperationService,
        IFileSystem fs,
        string? directory = null,
        IClockService? clock = null)
        : base() {
        Diag.WriteLine("[DI] FileCronTaskStore.ctor start");
        var dir = directory ?? AppDataConstants.Paths.CronTasksDirectory;
        if (string.IsNullOrWhiteSpace(dir))
            throw new ArgumentException("Directory cannot be null or empty", nameof(directory));

        _baseDir = dir;
        _filePath = Path.Combine(dir, AppDataConstants.ScheduledTasksFileName);
        _fileOperationService = fileOperationService ?? throw new ArgumentNullException(nameof(fileOperationService));
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _clock = clock ?? SystemClockService.Instance;

        Diag.WriteLine("[DI] FileCronTaskStore.ctor calling InitializeWatcher...");
        InitializeWatcher();
        Diag.WriteLine("[DI] FileCronTaskStore.ctor done");
    }


    /// <summary>
    /// 设置会话隔离标识 — 重新计算文件路径并重新初始化 watcher。
    /// 路径变为 {_baseDir}/{sessionId}/{ScheduledTasksFileName}。
    /// </summary>
    public void SetSessionId(string sessionId) {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        _ = _watcher?.DisposeAsync();
        _watcher = null;
        var sessionDir = Path.Combine(_baseDir, sessionId);
        _filePath = Path.Combine(sessionDir, AppDataConstants.ScheduledTasksFileName);
        InitializeWatcher();
    }

    private void InitializeWatcher() {
        var directory = Path.GetDirectoryName(_filePath);
        if (string.IsNullOrEmpty(directory)) return;

        _fs.CreateDirectory(directory);

        _watcher = _fs.Watch(directory, Path.GetFileName(_filePath));
        _watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime;
        _watcher.DebounceInterval = TimeSpan.FromMilliseconds(300);

        _watcher.DebouncedChanged += OnFileChanged;
        _watcher.DebouncedCreated += OnFileChanged;
        _watcher.DebouncedDeleted += OnFileDeleted;
        _watcher.EnableRaisingEvents = true;
    }

    private void OnFileChanged(object? sender, FileChangedEventArgs e) {
    }

    private void OnFileDeleted(object? sender, FileChangedEventArgs e) {
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<CronTask>> GetAllTasksAsync(CancellationToken ct = default) {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        var tcs = TcsFactory.Create<IReadOnlyList<CronTask>>();
        await SendAsync(new GetAllTasksCmd(ct, tcs), ct).ConfigureAwait(false);
        return await AskAwait(tcs, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<CronTask> AddTaskAsync(CreateCronTaskRequest request, CancellationToken ct = default) {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);

        if (!CronExpressionParser.IsValid(request.CronExpression))
            throw new ArgumentException("Invalid cron expression", nameof(request));

        var tcs = TcsFactory.Create<CronTask>();
        await SendAsync(new AddTaskCmd(request, ct, tcs), ct).ConfigureAwait(false);
        return await AskAwait(tcs, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task RemoveTasksAsync(IEnumerable<string> ids, CancellationToken ct = default) {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        var idSet = new HashSet<string>(ids);
        if (idSet.Count == 0) return;

        var tcs = TcsFactory.Create();
        await SendAsync(new RemoveTasksCmd(idSet, ct, tcs), ct).ConfigureAwait(false);
        await AskAwait(tcs, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task MarkTasksFiredAsync(IEnumerable<string> ids, long firedAt, CancellationToken ct = default) {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        var idSet = new HashSet<string>(ids);
        if (idSet.Count == 0) return;

        var tcs = TcsFactory.Create();
        await SendAsync(new MarkTasksFiredCmd(idSet, firedAt, ct, tcs), ct).ConfigureAwait(false);
        await AskAwait(tcs, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<CronTask?> GetTaskByIdAsync(string id, CancellationToken ct = default) {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        var tcs = TcsFactory.Create<CronTask?>();
        await SendAsync(new GetTaskByIdCmd(id, ct, tcs), ct).ConfigureAwait(false);
        return await AskAwait(tcs, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<CronTask>> GetTasksByAgentIdAsync(string agentId, CancellationToken ct = default) {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        var tcs = TcsFactory.Create<IReadOnlyList<CronTask>>();
        await SendAsync(new GetTasksByAgentIdCmd(agentId, ct, tcs), ct).ConfigureAwait(false);
        return await AskAwait(tcs, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Actor Consumer — 线程独占 _sessionTasks 和文件 I/O，串行处理命令，无需锁。
    /// </summary>
    protected override async ValueTask HandleAsync(ICronStoreCommand command, CancellationToken ct) {
        switch (command) {
            case GetAllTasksCmd cmd: {
                var fileTasks = await ReadFileTasksAsync(cmd.Ct).ConfigureAwait(false);
                var allTasks = new List<CronTask>(fileTasks);
                allTasks.AddRange(_sessionTasks.Values);
                cmd.Tcs.TrySetResult(allTasks);
                break;
            }

            case AddTaskCmd cmd: {
                var task = new CronTask {
                    Id = GenerateTaskId(),
                    CronExpression = cmd.Request.CronExpression,
                    Prompt = cmd.Request.Prompt,
                    CreatedAt = _clock.GetUtcNowOffset().ToUnixTimeMilliseconds(),
                    IsRecurring = cmd.Request.IsRecurring,
                    IsDurable = cmd.Request.IsDurable,
                    AgentId = cmd.Request.AgentId
                };

                if (!cmd.Request.IsDurable) {
                    _sessionTasks[task.Id] = task;
                } else {
                    var tasks = await ReadFileTasksAsync(cmd.Ct).ConfigureAwait(false);
                    var taskList = tasks.ToList();
                    taskList.Add(task);
                    var json = SerializeTasks(taskList);
                    await WriteJsonAsync(json, cmd.Ct).ConfigureAwait(false);
                }

                cmd.Tcs.TrySetResult(task);
                break;
            }

            case RemoveTasksCmd cmd: {
                foreach (var id in cmd.Ids) {
                    _sessionTasks.Remove(id);
                }

                var fileTasks = await ReadFileTasksAsync(cmd.Ct).ConfigureAwait(false);
                var originalCount = fileTasks.Count;
                var filteredTasks = fileTasks.Where(t => !cmd.Ids.Contains(t.Id)).ToList();

                if (filteredTasks.Count < originalCount) {
                    var json = SerializeTasks(filteredTasks);
                    await WriteJsonAsync(json, cmd.Ct).ConfigureAwait(false);
                }

                cmd.Tcs.TrySetResult();
                break;
            }

            case MarkTasksFiredCmd cmd: {
                foreach (var task in _sessionTasks.Values.Where(t => cmd.Ids.Contains(t.Id))) {
                    task.LastFiredAt = cmd.FiredAt;
                }

                var fileTasks = await ReadFileTasksAsync(cmd.Ct).ConfigureAwait(false);
                var changed = false;

                foreach (var task in fileTasks.Where(t => cmd.Ids.Contains(t.Id))) {
                    task.LastFiredAt = cmd.FiredAt;
                    changed = true;
                }

                if (changed) {
                    var json = SerializeTasks(fileTasks);
                    await WriteJsonAsync(json, cmd.Ct).ConfigureAwait(false);
                }

                cmd.Tcs.TrySetResult();
                break;
            }

            case GetTaskByIdCmd cmd: {
                if (_sessionTasks.TryGetValue(cmd.Id, out var sessionTask)) {
                    cmd.Tcs.TrySetResult(sessionTask);
                    break;
                }

                var fileTasks = await ReadFileTasksAsync(cmd.Ct).ConfigureAwait(false);
                cmd.Tcs.TrySetResult(fileTasks.FirstOrDefault(t => t.Id == cmd.Id));
                break;
            }

            case GetTasksByAgentIdCmd cmd: {
                var fileTasks = await ReadFileTasksAsync(cmd.Ct).ConfigureAwait(false);
                var result = new List<CronTask>();

                foreach (var t in _sessionTasks.Values) {
                    if (t.AgentId == cmd.AgentId)
                        result.Add(t);
                }

                foreach (var t in fileTasks) {
                    if (t.AgentId == cmd.AgentId && !_sessionTasks.ContainsKey(t.Id))
                        result.Add(t);
                }

                cmd.Tcs.TrySetResult(result);
                break;
            }
        }
    }

    /// <summary>命令消费者发生异常时的回调处理，输出诊断日志。</summary>
    /// <param name="ex">消费者抛出的异常。</param>
    protected override void OnConsumerError(Exception ex) {
        Diag.WriteLine($"[FileCronTaskStore] Actor Consumer 异常: {ex.Message}");
    }

    private async Task<IReadOnlyList<CronTask>> ReadFileTasksAsync(CancellationToken ct) {
        var result = await _fileOperationService.ReadFileAsync(_filePath, cancellationToken: ct).ConfigureAwait(false);
        if (!result.Success)
            return Array.Empty<CronTask>();

        try {
            var file = RelaxedJsonSerializer.Deserialize(result.Content, SchedulingIndentedJsonContext.Default.CronTaskFile);

            if (file?.Tasks == null)
                return Array.Empty<CronTask>();

            var validTasks = file.Tasks
                .Where(task => ValidateTask(task) && CronExpressionParser.IsValid(task.CronExpression))
                .ToList();

            return validTasks;
        } catch (JsonException) {
            return Array.Empty<CronTask>();
        }
    }

    private static string SerializeTasks(IReadOnlyList<CronTask> tasks) {
        var file = new CronTaskFile { Tasks = tasks.ToList() };
        return RelaxedJsonSerializer.Serialize(file, SchedulingIndentedJsonContext.Default);
    }

    private async Task WriteJsonAsync(string json, CancellationToken ct) {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory)) {
            _fs.CreateDirectory(directory);
        }

        await _fileOperationService.WriteFileAsync(_filePath, json, ct).ConfigureAwait(false);
    }

    private static bool ValidateTask(CronTask task) {
        return !string.IsNullOrEmpty(task.Id)
            && !string.IsNullOrEmpty(task.CronExpression)
            && !string.IsNullOrEmpty(task.Prompt)
            && task.CreatedAt > 0;
    }

    private static string GenerateTaskId() {
        return Guid.NewGuid().ToString("N")[..8];
    }

    /// <summary>
    /// 异步释放文件 watcher 和 Actor 资源
    /// </summary>
    public override async ValueTask DisposeAsync() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        if (_watcher is not null) await _watcher.DisposeAsync().ConfigureAwait(false);
        await base.DisposeAsync().ConfigureAwait(false);
    }
}