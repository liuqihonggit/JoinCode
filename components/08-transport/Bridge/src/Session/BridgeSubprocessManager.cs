
namespace Core.Bridge;

// BridgeSubprocessStatus 已迁移到 JoinCode.Transport 命名空间 (Transport.Contracts)

/// <summary>
/// 子进程句柄 — 对齐 TS 端 SessionHandle
/// 封装子进程的生命周期和通信接口
/// 通过 IProcessService.StartInteractiveAsync 创建进程，支持 JCC_PROCESS_MODE 环境变量切换
/// </summary>
public sealed class BridgeSubprocessHandle : IAsyncDisposable
{
    private readonly IInteractiveProcess _process;
    private readonly TaskCompletionSource<BridgeSubprocessStatus> _doneTcs;
    private readonly SemaphoreSlim _stdinLock;
    private readonly Queue<string> _stderrQueue;
    private readonly Queue<string> _activityQueue;
    private readonly ILogger? _logger;
    private readonly CancellationTokenSource _readCts;
    private Task? _stdoutReadTask;
    private int _isDisposed;
    private int _sigkillSent;
    private StreamWriter? _transcriptStream;
    private bool _firstUserMessageSeen; // 对齐 TS 端 firstUserMessageSeen — 标题获取一次性回调

    /// <summary>会话 ID</summary>
    public string SessionId { get; }

    /// <summary>进程退出 Promise — 对齐 TS 端 done</summary>
    public Task<BridgeSubprocessStatus> Done => _doneTcs.Task;

    /// <summary>访问令牌（可动态更新）</summary>
    public string? AccessToken { get; set; }

    /// <summary>最近的活动 — 对齐 TS 端 activities</summary>
    public IReadOnlyList<string> Activities => _activityQueue.ToList();

    /// <summary>最近的 stderr 输出 — 对齐 TS 端 lastStderr</summary>
    public IReadOnlyList<string> StderrLines => _stderrQueue.ToList();

    /// <summary>当前活动 — 对齐 TS 端 currentActivity</summary>
    public string? CurrentActivity { get; private set; }

    /// <summary>
    /// 首条用户消息回调 — 对齐 TS 端 SessionSpawnOpts.onFirstUserMessage
    /// 检测到第一条真实用户消息时触发一次（跳过 tool-result/synthetic/replay）
    /// </summary>
    public Action<string>? OnFirstUserMessage { get; set; }

    /// <summary>
    /// 权限请求回调 — 对齐 TS 端 deps.onPermissionRequest
    /// 检测到 control_request/can_use_tool 时触发
    /// 参数: permissionRequest, accessToken
    /// </summary>
    public Action<BridgePermissionRequest, string?>? OnPermissionRequest { get; set; }

    /// <summary>
    /// 活动回调 — 对齐 TS 端 deps.onActivity
    /// 检测到 assistant/result 活动时触发
    /// 参数: activity
    /// </summary>
    public Action<BridgeNdjsonActivity>? OnActivity { get; set; }

    /// <summary>进程是否仍在运行</summary>
    public bool IsRunning
    {
        get
        {
            try { return !_process.HasExited; }
            catch { return false; }
        }
    }

    /// <summary>设置 transcript 流 — 用于对齐 TS 端 transcript 写入</summary>
    public void SetTranscriptStream(StreamWriter stream)
    {
        _transcriptStream = stream;
    }

    /// <summary>
    /// 私有构造 — 通过 CreateAsync 工厂方法创建
    /// </summary>
    private BridgeSubprocessHandle(IInteractiveProcess process, BridgeSubprocessOptions options, ILogger? logger)
    {
        _process = process;
        SessionId = options.SessionId;
        AccessToken = options.AccessToken;
        _logger = logger;
        _doneTcs = new TaskCompletionSource<BridgeSubprocessStatus>();
        _stdinLock = new SemaphoreSlim(1, 1);
        _stderrQueue = new Queue<string>(MaxStderrLines);
        _activityQueue = new Queue<string>(MaxActivities);
        _readCts = new CancellationTokenSource();

        _process.ErrorDataReceived += OnErrorDataReceived;

        _stdoutReadTask = ReadStdoutAsync(_readCts.Token);
        _ = MonitorExitAsync(_readCts.Token);
    }

    /// <summary>
    /// 异步工厂方法 — 通过 IProcessService 创建子进程
    /// </summary>
    public static async Task<BridgeSubprocessHandle> CreateAsync(
        BridgeSubprocessOptions options,
        IProcessService processService,
        ILogger? logger = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.ExecPath);
        ArgumentNullException.ThrowIfNull(processService);

        var interactiveOptions = new InteractiveProcessOptions
        {
            FileName = options.ExecPath,
            Arguments = options.Arguments ?? string.Empty,
            WorkingDirectory = options.Dir,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
            StandardInputEncoding = System.Text.Encoding.UTF8,
            EnvironmentVariables = options.EnvironmentVariables
        };

        var process = await processService.StartInteractiveAsync(interactiveOptions, ct).ConfigureAwait(false);
        return new BridgeSubprocessHandle(process, options, logger);
    }

    private void OnErrorDataReceived(object? sender, string line)
    {
        EnqueueBounded(_stderrQueue, line, MaxStderrLines);
    }

    private async Task MonitorExitAsync(CancellationToken ct)
    {
        try
        {
            await _process.WaitForExitAsync(ct).ConfigureAwait(false);
            var exitCode = _process.ExitCode;
            var status = exitCode == 0
                ? BridgeSubprocessStatus.Completed
                : BridgeSubprocessStatus.Failed;

            _doneTcs.TrySetResult(status);
            _logger?.LogInformation("[SubprocessHandle] 进程退出: {SessionId}, 退出码={ExitCode}, 状态={Status}",
                SessionId, exitCode, status);
        }
        catch (OperationCanceledException)
        {
            _doneTcs.TrySetResult(BridgeSubprocessStatus.Failed);
        }
        catch (Exception ex)
        {
            _doneTcs.TrySetResult(BridgeSubprocessStatus.Failed);
            _logger?.LogWarning(ex, "[SubprocessHandle] 监控进程退出异常: {SessionId}", SessionId);
        }
    }

    /// <summary>
    /// 向子进程 stdin 写入数据 — 对齐 TS 端 writeStdin
    /// </summary>
    public async Task WriteStdinAsync(string data, CancellationToken ct = default)
    {
        await _stdinLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_process.StandardInput.BaseStream is null || !_process.StandardInput.BaseStream.CanWrite)
            {
                _logger?.LogWarning("[SubprocessHandle] stdin 不可写");
                return;
            }

            await _process.StandardInput.WriteAsync(data.AsMemory(), ct).ConfigureAwait(false);
            await _process.StandardInput.FlushAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[SubprocessHandle] 写入 stdin 失败");
        }
        finally
        {
            _stdinLock.Release();
        }
    }

    /// <summary>
    /// 刷新访问令牌 — 对齐 TS 端 updateAccessToken
    /// 通过 stdin 发送 update_environment_variables 消息
    /// </summary>
    public async Task UpdateAccessTokenAsync(string newToken, CancellationToken ct = default)
    {
        AccessToken = newToken;
        var message = $"{{\"type\":\"update_environment_variables\",\"variables\":{{\"JCC_API_KEY\":\"{newToken}\"}}}}\n";
        await WriteStdinAsync(message, ct).ConfigureAwait(false);
        _logger?.LogDebug("[SubprocessHandle] 令牌已刷新: {SessionId}", SessionId);
    }

    /// <summary>
    /// 优雅停止 — 对齐 TS 端 kill()
    /// </summary>
    public void Kill()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill();
                _logger?.LogInformation("[SubprocessHandle] 已发送终止信号: {SessionId}", SessionId);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[SubprocessHandle] 终止进程失败");
        }
    }

    /// <summary>
    /// 强制杀死 — 对齐 TS 端 forceKill()
    /// </summary>
    public void ForceKill()
    {
        if (Interlocked.Exchange(ref _sigkillSent, 1) == 1)
        {
            return;
        }

        try
        {
            if (!_process.HasExited)
            {
                _process.Kill();
                _logger?.LogWarning("[SubprocessHandle] 已强制终止: {SessionId}", SessionId);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[SubprocessHandle] 强制终止失败");
        }
    }

    /// <summary>
    /// stdout 行接收事件 — NDJSON 消息
    /// </summary>
    public event EventHandler<string>? OutputLineReceived;

    private const int MaxStderrLines = 10;
    private const int MaxActivities = 10;

    /// <summary>异步读取 stdout（NDJSON 行）— 消费 StandardOutput 流</summary>
    private async Task ReadStdoutAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var line = await _process.StandardOutput.ReadLineAsync(ct).ConfigureAwait(false);
                if (line is null) break;

                EnqueueBounded(_activityQueue, line, MaxActivities);
                CurrentActivity = line;

                // 对齐 TS 端: 写入 transcript 文件
                if (_transcriptStream is not null)
                {
                    try
                    {
                        _transcriptStream.WriteLine(line);
                        _transcriptStream.Flush();
                    }
                    catch (Exception ex)
                    {
                        // transcript 写入失败不阻塞
                        System.Diagnostics.Trace.WriteLine($"[BridgeSubprocessManager] Transcript write failed: {ex.Message}");
                    }
                }

                // 对齐 TS 端 sessionRunner.ts: 检测首条用户消息 — onFirstUserMessage 回调
                if (!_firstUserMessageSeen && OnFirstUserMessage is not null)
                {
                    var userText = ExtractUserMessageText(line);
                    if (userText is not null)
                    {
                        _firstUserMessageSeen = true;
                        OnFirstUserMessage(userText);
                    }
                }

                // 对齐 TS 端 sessionRunner.ts: extractActivities — 提取活动信息
                if (OnActivity is not null)
                {
                    var extractedActivities = BridgeNdjsonParser.ExtractActivities(line);
                    foreach (var activity in extractedActivities)
                    {
                        OnActivity(activity);
                    }
                }

                // 对齐 TS 端 sessionRunner.ts: control_request 检测 — 权限请求
                if (OnPermissionRequest is not null)
                {
                    var permReq = BridgeNdjsonParser.ExtractPermissionRequest(line);
                    if (permReq is not null)
                    {
                        OnPermissionRequest(permReq, AccessToken);
                    }
                }

                // 通知外部
                OutputLineReceived?.Invoke(this, line);
            }
        }
        catch (OperationCanceledException)
        {
            // 正常取消
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "[SubprocessHandle] stdout 读取结束");
        }
    }

    /// <summary>
    /// 从 NDJSON 行提取用户消息文本 — 对齐 TS 端 extractUserMessageText
    /// 跳过 tool-result、synthetic、replay 消息，只保留真实人类输入
    /// </summary>
    internal static string? ExtractUserMessageText(string ndjsonLine)
    {
        if (string.IsNullOrWhiteSpace(ndjsonLine)) return null;

        try
        {
            var json = JsonSerializer.Deserialize(ndjsonLine, BridgeJsonContext.Default.DictionaryStringJsonElement);
            if (json is null) return null;

            // 必须是 user 类型
            if (!json.TryGetValue("type", out var typeEl) || typeEl.ValueKind != JsonValueKind.String) return null;
            var type = typeEl.GetString();
            if (!string.Equals(type, "user", StringComparison.OrdinalIgnoreCase)) return null;

            // 跳过 tool-result 消息 — 对齐 TS 端: message.role === 'tool-result'
            if (json.TryGetValue("role", out var roleEl) && roleEl.ValueKind == JsonValueKind.String)
            {
                var role = roleEl.GetString();
                if (string.Equals(role, "tool-result", StringComparison.OrdinalIgnoreCase)) return null;
            }

            // 跳过 synthetic 消息 — 对齐 TS 端: message.synthetic === true
            if (json.TryGetValue("synthetic", out var synthEl) && synthEl.ValueKind == JsonValueKind.True) return null;

            // 跳过 replay 消息 — 对齐 TS 端: message.source === 'replay'
            if (json.TryGetValue("source", out var sourceEl) && sourceEl.ValueKind == JsonValueKind.String)
            {
                var source = sourceEl.GetString();
                if (string.Equals(source, "replay", StringComparison.OrdinalIgnoreCase)) return null;
            }

            // 提取文本内容 — 对齐 TS 端: extractUserMessageText
            if (json.TryGetValue("content", out var contentEl))
            {
                if (contentEl.ValueKind == JsonValueKind.String)
                {
                    var text = contentEl.GetString();
                    return string.IsNullOrWhiteSpace(text) ? null : text;
                }

                if (contentEl.ValueKind == JsonValueKind.Array)
                {
                    // content 是数组，提取第一个 text 类型的 block
                    foreach (var item in contentEl.EnumerateArray())
                    {
                        if (item.TryGetProperty("type", out var blockTypeEl) &&
                            blockTypeEl.ValueKind == JsonValueKind.String &&
                            string.Equals(blockTypeEl.GetString(), "text", StringComparison.OrdinalIgnoreCase))
                        {
                            if (item.TryGetProperty("text", out var textEl) && textEl.ValueKind == JsonValueKind.String)
                            {
                                var text = textEl.GetString();
                                return string.IsNullOrWhiteSpace(text) ? null : text;
                            }
                        }
                    }
                }
            }

            // 兜底: 尝试 message.content（嵌套结构）
            if (json.TryGetValue("message", out var msgEl) && msgEl.ValueKind == JsonValueKind.Object)
            {
                if (msgEl.TryGetProperty("content", out var msgContentEl))
                {
                    if (msgContentEl.ValueKind == JsonValueKind.String)
                    {
                        var text = msgContentEl.GetString();
                        return string.IsNullOrWhiteSpace(text) ? null : text;
                    }
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>有界入队 — 超出容量时出队最旧元素</summary>
    private static void EnqueueBounded(Queue<string> queue, string item, int maxCount)
    {
        while (queue.Count >= maxCount)
        {
            queue.Dequeue();
        }
        queue.Enqueue(item);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 1)
        {
            return;
        }

        // 取消读取任务
        await _readCts.CancelAsync().ConfigureAwait(false);

        try
        {
            if (!_process.HasExited)
            {
                Kill();

                // 等待最多 5 秒
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try
                {
                    await _process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    ForceKill();
                }
            }
        }
        catch (Exception ex)
        {
            // Dispose 时忽略异常
            System.Diagnostics.Trace.WriteLine($"[BridgeSubprocessManager] Process wait for exit failed on dispose: {ex.Message}");
        }

        // 等待读取任务完成
        var readTasks = new List<Task>(1);
        if (_stdoutReadTask is not null) readTasks.Add(_stdoutReadTask);
        if (readTasks.Count > 0)
        {
            try
            {
                await Task.WhenAll(readTasks).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // 忽略读取任务异常
                System.Diagnostics.Trace.WriteLine($"[BridgeSubprocessManager] Read task exception on dispose: {ex.Message}");
            }
        }

        _readCts.Dispose();
        await _process.DisposeAsync().ConfigureAwait(false);
        _stdinLock.Dispose();

        // 关闭 transcript 流，避免资源泄漏
        try
        {
            _transcriptStream?.Dispose();
            _transcriptStream = null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[BridgeSubprocessManager] Transcript stream dispose failed: {ex.Message}");
        }
    }
}

/// <summary>
/// 子进程生成器 — 对齐 TS 端 createSessionSpawner
/// 负责生成 jcc.exe 子进程并管理其生命周期
/// ProcessStartInfo 由 BridgeSubprocessHandle 内部创建，避免 JCC3004 分析器误报
/// </summary>
public sealed class BridgeSubprocessSpawner
{
    private readonly ILogger? _logger;
    private readonly IFileSystem _fs;
    private readonly IProcessService _processService;

    /// <summary>jcc 可执行文件路径</summary>
    public string ExecPath { get; init; } = "jcc";

    /// <summary>工作目录</summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>额外环境变量</summary>
    public Dictionary<string, string>? ExtraEnv { get; init; }

    /// <summary>是否详细日志</summary>
    public bool Verbose { get; init; }

    /// <summary>关闭等待超时（毫秒）</summary>
    public int ShutdownGraceMs { get; init; } = 30000;

    public BridgeSubprocessSpawner(IFileSystem fs, IProcessService processService, ILogger? logger = null)
    {
        _fs = fs;
        _processService = processService;
        _logger = logger;
    }

    /// <summary>
    /// 生成子进程 — 对齐 TS 端 SessionSpawner.spawn()
    /// BridgeSubprocessHandle 内部创建 ProcessStartInfo + 消费 StandardError/StandardOutput
    /// 包含 transcript 文件写入、safeFilenameId 净化、debugFile 解析
    /// </summary>
    public async Task<BridgeSubprocessHandle> SpawnAsync(BridgeSubprocessOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        // 对齐 TS 端: safeFilenameId 净化会话 ID
        var safeId = SafeFilenameId(options.SessionId);

        // 对齐 TS 端: debugFile 解析 + transcript 路径
        string? debugFile = null;
        string? transcriptPath = null;

        if (!string.IsNullOrEmpty(options.DebugFile))
        {
            var extIdx = options.DebugFile.LastIndexOf('.');
            if (extIdx > 0)
            {
                debugFile = $"{options.DebugFile[..extIdx]}-{safeId}{options.DebugFile[extIdx..]}";
            }
            else
            {
                debugFile = $"{options.DebugFile}-{safeId}";
            }

            // 对齐 TS 端: bridge-transcript-{safeId}.jsonl
            var debugDir = Path.GetDirectoryName(debugFile);
            transcriptPath = string.IsNullOrEmpty(debugDir)
                ? $"bridge-transcript-{safeId}.jsonl"
                : Path.Combine(debugDir, $"bridge-transcript-{safeId}.jsonl");
        }
        else if (options.Verbose || IsAntBuild())
        {
            var tempDir = Path.GetTempPath();
            debugFile = Path.Combine(tempDir, AppDataConstants.AppDataFolder, $"bridge-session-{safeId}.log");
        }

        // 构建带 transcript 的路径
        var args = BuildArguments(new BridgeSubprocessOptions
        {
            SessionId = options.SessionId,
            SdkUrl = options.SdkUrl,
            AccessToken = options.AccessToken,
            Dir = options.Dir,
            UseCcrV2 = options.UseCcrV2,
            WorkerEpoch = options.WorkerEpoch,
            PermissionMode = options.PermissionMode,
            DebugFile = debugFile,
            Verbose = options.Verbose,
            Sandbox = options.Sandbox,
            ScriptArgs = options.ScriptArgs,
        });

        var envVars = BuildEnvironmentVariables(options);

        var workDir = options.Dir ?? WorkingDirectory ?? _fs.GetCurrentDirectory();

        _logger?.LogInformation("[SubprocessSpawner] 生成子进程: {ExecPath} {Args}", ExecPath, args);

        if (!string.IsNullOrEmpty(debugFile))
        {
            _logger?.LogDebug("[SubprocessSpawner] Debug log: {DebugFile}", debugFile);
        }

        // BridgeSubprocessHandle 内部创建 ProcessStartInfo + Process 并消费 StandardError/StandardOutput
        var handleOptions = new BridgeSubprocessOptions
        {
            SessionId = options.SessionId,
            ExecPath = ExecPath,
            Arguments = args,
            EnvironmentVariables = envVars,
            Dir = workDir,
            SdkUrl = options.SdkUrl,
            AccessToken = options.AccessToken,
            UseCcrV2 = options.UseCcrV2,
            WorkerEpoch = options.WorkerEpoch,
            PermissionMode = options.PermissionMode,
            DebugFile = debugFile,
            Verbose = options.Verbose,
            Sandbox = options.Sandbox,
            ScriptArgs = options.ScriptArgs,
            OnFirstUserMessage = options.OnFirstUserMessage,
            OnPermissionRequest = options.OnPermissionRequest,
            OnActivity = options.OnActivity,
        };

        var handle = await BridgeSubprocessHandle.CreateAsync(handleOptions, _processService, _logger, cancellationToken).ConfigureAwait(false);
        handle.OnFirstUserMessage = options.OnFirstUserMessage;
        handle.OnPermissionRequest = options.OnPermissionRequest;
        handle.OnActivity = options.OnActivity;

        // 对齐 TS 端: 初始化 transcript stream
        if (!string.IsNullOrEmpty(transcriptPath))
        {
            try
            {
                // 确保目录存在
                var dir = Path.GetDirectoryName(transcriptPath);
                if (!string.IsNullOrEmpty(dir) && !_fs.DirectoryExists(dir))
                {
                    _fs.CreateDirectory(dir);
                }

                // FileMode.Append 在 .NET 5+ 中文件不存在时抛 FileNotFoundException
                // 需要先确保文件存在
                if (!_fs.FileExists(transcriptPath))
                {
                    try
                    {
                        _fs.Open(transcriptPath, FileMode.CreateNew).Dispose();
                    }
                    catch (IOException ex) when (_fs.FileExists(transcriptPath))
                    {
                        // TOCTOU 竞态：其他进程在我们检查和创建之间已创建了文件 — 安全忽略
                        _logger?.LogDebug(ex, "Transcript file already exists (created by another process): {Path}", transcriptPath);
                    }
                }

                var stream = new StreamWriter(_fs.Open(transcriptPath, FileMode.Append));
                _logger?.LogDebug("[SubprocessSpawner] Transcript log: {Path}", transcriptPath);

                // 将 transcript stream 注入 handle（handle 内部在 stdout 读取时写入）
                handle.SetTranscriptStream(stream);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "[SubprocessSpawner] Transcript 写入初始化失败（非致命）");
            }
        }

        _logger?.LogInformation("[SubprocessSpawner] 子进程已启动: SessionId={SessionId}", options.SessionId);

        return handle;
    }

    /// <summary>对齐 TS 端: safeFilenameId — 去除非法文件名字符</summary>
    public static string SafeFilenameId(string id)
    {
        return System.Text.RegularExpressions.Regex.Replace(id, @"[^a-zA-Z0-9_-]", "_");
    }

    /// <summary>检测是否为 Ant 构建</summary>
    private static bool IsAntBuild()
    {
        return Environment.GetEnvironmentVariable("USER_TYPE") == "ant";
    }

    /// <summary>
    /// 构建环境变量字典 — 对齐 TS 端子进程环境变量
    /// 包含 CLAUDE_CODE_SESSION_ACCESS_TOKEN、CLAUDE_CODE_POST_FOR_SESSION_INGRESS_V2 等
    /// </summary>
    private Dictionary<string, string> BuildEnvironmentVariables(BridgeSubprocessOptions options)
    {
        var env = new Dictionary<string, string>();

        // 额外环境变量
        if (ExtraEnv is not null)
        {
            foreach (var (key, value) in ExtraEnv)
            {
                env[key] = value;
            }
        }

        // Bridge 专用环境变量 — 对齐 TS 端
        env["JCC_ENVIRONMENT_KIND"] = "bridge";

        if (options.AccessToken is not null)
        {
            env["JCC_API_KEY"] = options.AccessToken;
            // 对齐 TS 端: CLAUDE_CODE_SESSION_ACCESS_TOKEN
            env["CLAUDE_CODE_SESSION_ACCESS_TOKEN"] = options.AccessToken;
        }

        // 剥离 bridge 的 OAuth token，子进程使用 session token
        env["CLAUDE_CODE_OAUTH_TOKEN"] = "";

        // v1: HybridTransport (WS reads + POST writes) to Session-Ingress
        env["CLAUDE_CODE_POST_FOR_SESSION_INGRESS_V2"] = "1";

        if (options.UseCcrV2)
        {
            env["CLAUDE_CODE_USE_CCR_V2"] = "1";
            if (options.WorkerEpoch.HasValue)
            {
                env["CLAUDE_CODE_WORKER_EPOCH"] = options.WorkerEpoch.Value.ToString();
            }
        }

        if (options.Sandbox)
        {
            env["CLAUDE_CODE_FORCE_SANDBOX"] = "1";
        }

        return env;
    }

    /// <summary>
    /// 构建命令行参数 — 对齐 TS 端子进程参数
    /// </summary>
    private static string BuildArguments(BridgeSubprocessOptions options)
    {
        var sb = new StringBuilder();

        // 额外脚本参数 — 对齐 TS 端: [...deps.scriptArgs, ...]
        if (options.ScriptArgs is not null)
        {
            foreach (var arg in options.ScriptArgs)
            {
                sb.Append('"').Append(arg).Append("\" ").Append(' ');
            }
        }

        // --print 模式（非交互）
        sb.Append("--print");

        // --sdk-url
        if (options.SdkUrl is not null)
        {
            sb.Append(" --sdk-url \"").Append(options.SdkUrl).Append('"');
        }

        // --session-id
        if (options.SessionId is not null)
        {
            sb.Append(" --session-id \"").Append(options.SessionId).Append('"');
        }

        // --input-format stream-json
        sb.Append(" --input-format stream-json");

        // --output-format stream-json
        sb.Append(" --output-format stream-json");

        // --replay-user-messages — 对齐 TS 端
        sb.Append(" --replay-user-messages");

        // --verbose
        if (options.Verbose)
        {
            sb.Append(" --verbose");
        }

        // --debug-file — 对齐 TS 端: 当 debugFile 提供时写入
        if (!string.IsNullOrEmpty(options.DebugFile))
        {
            sb.Append(" --debug-file \"").Append(options.DebugFile).Append('"');
        }

        // --permission-mode
        if (!string.IsNullOrEmpty(options.PermissionMode))
        {
            sb.Append(" --permission-mode \"").Append(options.PermissionMode).Append('"');
        }

        return sb.ToString();
    }

    /// <summary>
    /// 优雅关闭所有子进程 — 对齐 TS 端 runBridgeLoop 的关闭流程
    /// </summary>
    public async Task ShutdownAllAsync(
        IReadOnlyList<BridgeSubprocessHandle> handles,
        CancellationToken ct = default)
    {
        if (handles.Count == 0) return;

        _logger?.LogInformation("[SubprocessSpawner] 关闭 {Count} 个子进程", handles.Count);

        // 1. 向所有进程发送 SIGTERM
        foreach (var handle in handles)
        {
            handle.Kill();
        }

        // 2. 等待优雅退出
        var graceTimeout = TimeSpan.FromMilliseconds(ShutdownGraceMs);
        using var graceCts = TimeoutHelper.CreateLinkedTimeout(ct, graceTimeout);

        try
        {
            var doneTasks = handles.Select(h => h.Done).ToArray();
            await Task.WhenAll(doneTasks).WaitAsync(graceCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 3. 强制杀死未退出的进程
            foreach (var handle in handles.Where(h => h.IsRunning))
            {
                handle.ForceKill();
            }
        }

        _logger?.LogInformation("[SubprocessSpawner] 所有子进程已关闭");
    }
}

/// <summary>
/// 子进程生成选项 — 对齐 TS 端 spawn 选项
/// </summary>
public sealed class BridgeSubprocessOptions
{
    /// <summary>会话 ID</summary>
    public required string SessionId { get; init; }

    /// <summary>可执行文件路径 — 由 Spawner 填充</summary>
    public string? ExecPath { get; init; }

    /// <summary>命令行参数 — 由 Spawner 填充</summary>
    public string? Arguments { get; init; }

    /// <summary>环境变量 — 由 Spawner 填充</summary>
    public Dictionary<string, string>? EnvironmentVariables { get; init; }

    /// <summary>SDK URL（WebSocket/SSE 端点）</summary>
    public string? SdkUrl { get; init; }

    /// <summary>访问令牌（JWT/OAuth）</summary>
    public string? AccessToken { get; init; }

    /// <summary>工作目录</summary>
    public string? Dir { get; init; }

    /// <summary>是否使用 CCR v2 模式</summary>
    public bool UseCcrV2 { get; init; }

    /// <summary>Worker epoch（v2 模式）</summary>
    public int? WorkerEpoch { get; init; }

    /// <summary>权限模式</summary>
    public string? PermissionMode { get; init; }

    /// <summary>调试文件路径 — 用于生成 transcript 日志</summary>
    public string? DebugFile { get; init; }

    /// <summary>是否详细日志</summary>
    public bool Verbose { get; init; }

    /// <summary>沙箱模式</summary>
    public bool Sandbox { get; init; }

    /// <summary>额外脚本参数 — 对齐 TS 端 scriptArgs</summary>
    public string[]? ScriptArgs { get; init; }

    /// <summary>
    /// 首条用户消息回调 — 对齐 TS 端 SessionSpawnOpts.onFirstUserMessage
    /// 子进程 stdout 检测到第一条真实用户消息时触发（跳过 tool-result/synthetic/replay）
    /// 用于派生会话标题
    /// </summary>
    public Action<string>? OnFirstUserMessage { get; init; }

    /// <summary>
    /// 权限请求回调 — 对齐 TS 端 deps.onPermissionRequest
    /// 子进程 stdout 检测到 control_request/can_use_tool 时触发
    /// 参数: permissionRequest, accessToken
    /// </summary>
    public Action<BridgePermissionRequest, string?>? OnPermissionRequest { get; init; }

    /// <summary>
    /// 活动回调 — 对齐 TS 端 deps.onActivity
    /// 子进程 stdout 检测到 assistant/result 活动时触发
    /// 参数: activity
    /// </summary>
    public Action<BridgeNdjsonActivity>? OnActivity { get; init; }
}
