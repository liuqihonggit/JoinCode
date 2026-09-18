namespace Services.Build;

/// <summary>
/// 编译队列基类 — 提供 BuildId 生成、条目/等待句柄管理、取消与失败结果构造、
/// 输出范围查询、防睡眠执行骨架等共享能力。
/// <para>串行模式(<see cref="BuildQueueService"/>)与多 Worker 模式(<see cref="BuildQueueRouter"/>)共用此基类,</para>
/// <para>各自实现 <see cref="SubmitAsync"/>/<see cref="CancelAsync"/>/<see cref="GetStatus"/>/<see cref="ClearCacheAsync"/>/<see cref="DisposeAsync"/> 等差异化逻辑。</para>
/// </summary>
public abstract class BuildQueueBase : IBuildQueueService
{
    /// <summary>条目与等待句柄的成对存储,保证同一 BuildId 生命周期一致。</summary>
    private protected readonly BuildQueueEntryStore _store = new();

    /// <summary>构建 ID 自增计数器,用于生成 b-{counter:D4} 格式 ID。</summary>
    protected int _buildCounter;

    /// <summary>释放标志,1 表示已释放。</summary>
    protected int _disposed;

    /// <summary>
    /// 生成下一个构建 ID,格式 <c>b-{counter:D4}</c>。
    /// </summary>
    /// <returns>新的构建 ID。</returns>
    protected string NextBuildId() => $"b-{Interlocked.Increment(ref _buildCounter):D4}";

    /// <summary>
    /// 创建已入队的编译条目并注册到 <see cref="_store"/>。
    /// </summary>
    /// <param name="request">编译请求。</param>
    /// <returns>已入队的条目及其等待句柄(调用方可继续写入 Channel 或路由到 Worker)。</returns>
    protected (BuildQueueEntry Entry, TaskCompletionSource<BuildQueueResult> Tcs) CreateQueuedEntry(BuildRequest request)
    {
        var entry = new BuildQueueEntry
        {
            BuildId = NextBuildId(),
            Request = request,
            Status = BuildQueueEntryStatus.Queued,
            QueuePosition = _store.Count
        };
        var tcs = new TaskCompletionSource<BuildQueueResult>();
        _store.Add(entry.BuildId, entry, tcs);
        return (entry, tcs);
    }

    /// <inheritdoc />
    public Task<BuildQueueResult> WaitAsync(string buildId, CancellationToken ct)
    {
        if (!_store.TryGetTcs(buildId, out var tcs))
            throw new InvalidOperationException($"Build {buildId} not found");
        return tcs.Task;
    }

    /// <inheritdoc />
    public BuildQueueEntry? GetBuild(string buildId)
    {
        return _store.TryGetEntry(buildId, out var entry) ? entry : null;
    }

    /// <inheritdoc />
    public string GetOutputRange(string buildId, int startLine, int endLine)
    {
        var entry = _store.TryGetEntry(buildId, out var e) ? e : null;
        if (entry?.Result is null)
            return $"Build {buildId} not found or has no result";

        var output = entry.Result.ExitCode == 0
            ? entry.Result.Output
            : $"{entry.Result.ErrorOutput}\n{entry.Result.Output}";

        var lines = output.Split('\n');
        if (endLine <= 0) endLine = lines.Length;

        startLine = Math.Max(1, startLine);
        endLine = Math.Min(lines.Length, endLine);

        if (startLine > endLine)
            return $"Invalid range: start={startLine}, end={endLine}, total={lines.Length}";

        var selected = lines[(startLine - 1)..endLine];
        return string.Join('\n', selected);
    }

    /// <summary>
    /// 构造取消结果并完成对应 BuildId 的等待句柄。
    /// </summary>
    /// <param name="buildId">构建 ID。</param>
    /// <param name="entry">编译条目。</param>
    protected void CompleteWithCancellation(string buildId, BuildQueueEntry entry)
    {
        var result = CreateCancelledResult(entry, "Build was cancelled");
        entry.Result = result;
        if (_store.TryGetTcs(buildId, out var tcs))
            tcs.TrySetResult(result);
    }

    /// <summary>
    /// 构造已取消的 <see cref="BuildQueueResult"/>(不完成等待句柄)。
    /// </summary>
    /// <param name="entry">编译条目。</param>
    /// <param name="message">取消原因(写入 <see cref="BuildQueueResult.ErrorOutput"/>)。</param>
    /// <returns>已取消的构建结果。</returns>
    protected internal static BuildQueueResult CreateCancelledResult(BuildQueueEntry entry, string message)
    {
        return new BuildQueueResult
        {
            BuildId = entry.BuildId,
            ExitCode = -1,
            Output = string.Empty,
            ErrorOutput = message,
            WaitDuration = entry.StartedAt.HasValue
                ? entry.StartedAt.Value - entry.Request.SubmittedAt
                : TimeSpan.Zero,
            BuildDuration = TimeSpan.Zero,
            QueuePosition = entry.QueuePosition,
            Cancelled = true
        };
    }

    /// <summary>
    /// 构造失败的 <see cref="BuildQueueResult"/>(异常信息写入 <see cref="BuildQueueResult.ErrorOutput"/>)。
    /// </summary>
    /// <param name="entry">编译条目。</param>
    /// <param name="ex">异常。</param>
    /// <returns>失败的构建结果。</returns>
    protected internal static BuildQueueResult CreateFailedResult(BuildQueueEntry entry, Exception ex)
    {
        return new BuildQueueResult
        {
            BuildId = entry.BuildId,
            ExitCode = -1,
            Output = string.Empty,
            ErrorOutput = ex.Message,
            WaitDuration = entry.StartedAt.HasValue
                ? entry.StartedAt.Value - entry.Request.SubmittedAt
                : TimeSpan.Zero,
            BuildDuration = TimeSpan.Zero,
            QueuePosition = entry.QueuePosition
        };
    }

    /// <summary>
    /// 编译执行核心骨架 — 防睡眠 → 执行 Bash → 睡眠检测 → 构造 <see cref="BuildQueueResult"/>。
    /// <para>子类在调用前可先获取跨进程锁等资源,然后将构建令牌传入此方法。</para>
    /// <para><see cref="BuildQueueResult.BuildDuration"/> 来源由 <paramref name="preferResultExecutionTime"/> 控制:</para>
    /// <para><c>true</c> 时使用 Actuator 返回的 <c>ExecutionTime</c>(多 Worker 模式),</para>
    /// <para><c>false</c> 时使用本地 <see cref="Stopwatch"/> 测量(串行模式)。</para>
    /// </summary>
    /// <param name="entry">编译条目。</param>
    /// <param name="actuatorRegistry">系统执行器注册表。</param>
    /// <param name="preventSleepService">防睡眠服务(可选)。</param>
    /// <param name="logger">日志记录器(可选)。</param>
    /// <param name="buildCt">构建取消令牌。</param>
    /// <param name="preferResultExecutionTime">是否优先使用 Actuator 返回的执行时长。</param>
    /// <returns>构建结果。</returns>
    protected internal static async Task<BuildQueueResult> ExecuteBuildCoreAsync(
        BuildQueueEntry entry,
        ISystemActuatorRegistry actuatorRegistry,
        IPreventSleepService? preventSleepService,
        ILogger? logger,
        CancellationToken buildCt,
        bool preferResultExecutionTime = false)
    {
        await using var sleepScope = await PreventSleepScope.CreateAsync(
            preventSleepService, cancellationToken: CancellationToken.None).ConfigureAwait(false);

        var sw = Stopwatch.StartNew();
        var wallStart = DateTimeOffset.UtcNow;

        var result = await actuatorRegistry.Get(SystemActuatorKind.Bash).ExecuteAsync(
            entry.Request.Command,
            workingDirectory: entry.Request.WorkingDirectory,
            cancellationToken: buildCt).ConfigureAwait(false);

        sw.Stop();
        var wallElapsed = DateTimeOffset.UtcNow - wallStart;

        var buildDuration = preferResultExecutionTime ? result.ExecutionTime : sw.Elapsed;
        var sleepDetected = wallElapsed > buildDuration + TimeSpan.FromSeconds(30);
        if (sleepDetected)
        {
            logger?.LogWarning(
                "Sleep detected during build {BuildId}: wall={Wall}, cpu={Cpu}",
                entry.BuildId, wallElapsed, buildDuration);
        }

        return new BuildQueueResult
        {
            BuildId = entry.BuildId,
            ExitCode = result.ExitCode ?? -1,
            Output = result.Stdout ?? string.Empty,
            ErrorOutput = result.Stderr ?? string.Empty,
            WaitDuration = entry.StartedAt.HasValue
                ? entry.StartedAt.Value - entry.Request.SubmittedAt
                : TimeSpan.Zero,
            BuildDuration = buildDuration,
            QueuePosition = entry.QueuePosition,
            SleepDetected = sleepDetected,
            Cancelled = buildCt.IsCancellationRequested
        };
    }

    /// <summary>
    /// 若已释放则抛出 <see cref="ObjectDisposedException"/>。
    /// </summary>
    /// <param name="typeName">类型名(用于异常消息)。</param>
    protected void ThrowIfDisposed(string typeName)
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(typeName);
    }

    /// <inheritdoc />
    public abstract Task<string> SubmitAsync(BuildRequest request, CancellationToken ct);

    /// <inheritdoc />
    public abstract Task<bool> CancelAsync(string buildId, CancellationToken ct);

    /// <inheritdoc />
    public abstract BuildQueueStatus GetStatus();

    /// <inheritdoc />
    public abstract Task ClearCacheAsync(CancellationToken ct);

    /// <inheritdoc />
    public abstract ValueTask DisposeAsync();
}
