
namespace Core.Bridge;

// BridgeSubprocessStatus 已迁移到 JoinCode.Transport 命名空间 (Transport.Contracts)

/// <summary>
/// 子进程句柄 — 对齐 TS 端 SessionHandle
/// 封装子进程的生命周期和通信接口
/// 通过 IProcessService.StartInteractiveAsync 创建进程，支持 JCC_PROCESS_MODE 环境变量切换
/// </summary>
public sealed class BridgeSubprocessHandle : PluginResourceBase {
    private readonly SubprocessIoChannels _io;
    private readonly SubprocessState _state;
    private readonly ILogger? _logger;

    /// <summary>会话 ID</summary>
    public new string SessionId { get; }

    /// <summary>进程退出 Promise — 对齐 TS 端 done</summary>
    public Task<BridgeSubprocessStatus> Done => _state.Done;

    /// <summary>访问令牌（可动态更新）</summary>
    public string? AccessToken { get; set; }

    /// <summary>最近的活动 — 对齐 TS 端 activities（遍历器，不分配新集合）</summary>
    public IEnumerable<string> Activities => _io.Activities;

    /// <summary>最近的 stderr 输出 — 对齐 TS 端 lastStderr（遍历器，不分配新集合）</summary>
    public IEnumerable<string> StderrLines => _io.StderrLines;

    /// <summary>当前活动 — 对齐 TS 端 currentActivity</summary>
    public string? CurrentActivity => _io.CurrentActivity;

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
    public bool IsRunning => _io.IsRunning;

    /// <summary>设置 transcript 流 — 用于对齐 TS 端 transcript 写入</summary>
    /// <param name="stream">transcript 写入流</param>
    public void SetTranscriptStream(StreamWriter stream) => _io.SetTranscriptStream(stream);

    /// <summary>
    /// 私有构造 — 通过 CreateAsync 工厂方法创建
    /// </summary>
    private BridgeSubprocessHandle(IInteractiveProcess process, BridgeSubprocessOptions options, ILogger? logger, ResilientSubprocess? resilientSubprocess = null)
        : base("Bridge", PluginResourceKind.Hook, options.SessionId) {
        SessionId = options.SessionId;
        AccessToken = options.AccessToken;
        _logger = logger;
        _state = new SubprocessState();
        _io = new SubprocessIoChannels(process, resilientSubprocess, logger, options.SessionId);

        var stdoutTask = ReadStdoutAsync(_io.ReadCancellationToken);
        _io.SetStdoutReadTask(stdoutTask);
        _ = MonitorExitAsync(_io.ReadCancellationToken);
    }

    /// <summary>
    /// 异步工厂方法 — 通过 IProcessService 创建子进程
    /// </summary>
    /// <param name="options">子进程选项</param>
    /// <param name="processService">进程服务</param>
    /// <param name="logger">日志记录器（可选）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>创建好的子进程句柄</returns>
    public static async Task<BridgeSubprocessHandle> CreateAsync(
        BridgeSubprocessOptions options,
        IProcessService processService,
        ILogger? logger = null,
        CancellationToken ct = default) {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.ExecPath);
        ArgumentNullException.ThrowIfNull(processService);

        var interactiveOptions = new InteractiveProcessOptions {
            FileName = options.ExecPath,
            Arguments = options.Arguments ?? string.Empty,
            ArgumentList = options.ArgumentList,
            WorkingDirectory = options.Dir,
            EnvironmentVariables = options.EnvironmentVariables
        };

        var process = await processService.StartInteractiveAsync(interactiveOptions, ct).ConfigureAwait(false);

        ResilientSubprocess? resilientSubprocess = null;
        var resilienceEnabled = Environment.GetEnvironmentVariable("JCC_RESILIENCE_ENABLED") is not "0";
        if (resilienceEnabled) {
            var policy = SubprocessResiliencePolicy.BridgeDefault;
            Func<CancellationToken, Task<IInteractiveProcess>> spawnFunc = async spawnCt =>
                await processService.StartInteractiveAsync(interactiveOptions, spawnCt).ConfigureAwait(false);
            resilientSubprocess = new ResilientSubprocess(process, spawnFunc, policy, logger);
        }

        return new BridgeSubprocessHandle(process, options, logger, resilientSubprocess);
    }

    private async Task MonitorExitAsync(CancellationToken ct) {
        try {
            await _io.WaitForExitAsync(ct).ConfigureAwait(false);
            var exitCode = _io.ExitCode;
            var status = exitCode == 0
                ? BridgeSubprocessStatus.Completed
                : BridgeSubprocessStatus.Failed;

            _state.TrySetDone(status);
            _logger?.LogInformation("[SubprocessHandle] 进程退出: {SessionId}, 退出码={ExitCode}, 状态={Status}",
                SessionId, exitCode, status);
        } catch (OperationCanceledException) {
            _state.TrySetDone(BridgeSubprocessStatus.Failed);
        } catch (Exception ex) {
            _state.TrySetDone(BridgeSubprocessStatus.Failed);
            _logger?.LogWarning(ex, "[SubprocessHandle] 监控进程退出异常: {SessionId}", SessionId);
        }
    }

    /// <summary>
    /// 向子进程 stdin 写入数据 — 对齐 TS 端 writeStdin
    /// </summary>
    /// <param name="data">写入的数据字符串</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    public async Task WriteStdinAsync(string data, CancellationToken ct = default) {
        await _io.WriteStdinAsync(data, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 刷新访问令牌 — 对齐 TS 端 updateAccessToken
    /// 通过 stdin 发送 update_environment_variables 消息
    /// </summary>
    /// <param name="newToken">新的访问令牌</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    public async Task UpdateAccessTokenAsync(string newToken, CancellationToken ct = default) {
        AccessToken = newToken;
        var message = $"{{\"type\":\"update_environment_variables\",\"variables\":{{\"JCC_SESSION_ACCESS_TOKEN\":\"{newToken}\"}}}}\n";
        await WriteStdinAsync(message, ct).ConfigureAwait(false);
        _logger?.LogDebug("[SubprocessHandle] 令牌已刷新: {SessionId}", SessionId);
    }

    /// <summary>
    /// 优雅停止 — 对齐 TS 端 kill()
    /// </summary>
    public void Kill() => _io.TryKillProcess("已发送终止信号", LogLevel.Information);

    /// <summary>
    /// 强制杀死 — 对齐 TS 端 forceKill()
    /// </summary>
    public void ForceKill() {
        if (!_state.TryMarkSigkillSent()) return;
        _io.TryKillProcess("已强制终止", LogLevel.Warning);
    }

    /// <summary>stdout 行接收事件 — NDJSON 消息</summary>
    public event EventHandler<string>? OutputLineReceived;

    /// <summary>异步读取 stdout（NDJSON 行）— 消费 StandardOutput 流</summary>
    private async Task ReadStdoutAsync(CancellationToken ct) {
        try {
            while (!ct.IsCancellationRequested) {
                var line = await _io.ReadStdoutLineAsync(ct).ConfigureAwait(false);
                if (line is null) break;

                _io.EnqueueActivity(line);
                _io.WriteTranscript(line);

                // 对齐 TS 端 sessionRunner.ts: 检测首条用户消息 — onFirstUserMessage 回调
                if (!_state.FirstUserMessageSeen && OnFirstUserMessage is not null) {
                    var userText = ExtractUserMessageText(line);
                    if (userText is not null) {
                        _state.MarkFirstUserMessageSeen();
                        OnFirstUserMessage(userText);
                    }
                }

                // 对齐 TS 端 sessionRunner.ts: extractActivities — 提取活动信息
                if (OnActivity is not null) {
                    var extractedActivities = BridgeNdjsonParser.ExtractActivities(line);
                    foreach (var activity in extractedActivities) {
                        OnActivity(activity);
                    }
                }

                // 对齐 TS 端 sessionRunner.ts: control_request 检测 — 权限请求
                if (OnPermissionRequest is not null) {
                    var permReq = BridgeNdjsonParser.ExtractPermissionRequest(line);
                    if (permReq is not null) {
                        OnPermissionRequest(permReq, AccessToken);
                    }
                }

                // 通知外部
                OutputLineReceived?.Invoke(this, line);
            }
        } catch (OperationCanceledException) {
            // 正常取消
        } catch (Exception ex) {
            _logger?.LogDebug(ex, "[SubprocessHandle] stdout 读取结束");
        }
    }

    /// <summary>
    /// 从 NDJSON 行提取用户消息文本 — 对齐 TS 端 extractUserMessageText
    /// 跳过 tool-result、synthetic、replay 消息，只保留真实人类输入
    /// </summary>
    /// <param name="ndjsonLine">NDJSON 行字符串</param>
    /// <returns>用户消息文本；非用户消息返回 null</returns>
    internal static string? ExtractUserMessageText(string ndjsonLine) {
        if (string.IsNullOrWhiteSpace(ndjsonLine)) return null;

        try {
            var json = RelaxedJsonSerializer.Deserialize(ndjsonLine, BridgeJsonContext.Default.DictionaryStringJsonElement);
            if (json is null) return null;

            // 必须是 user 类型
            if (!json.TryGetValue("type", out var typeEl) || typeEl.ValueKind != JsonValueKind.String) return null;
            var type = typeEl.GetString();
            if (!string.Equals(type, "user", StringComparison.OrdinalIgnoreCase)) return null;

            // 跳过 tool-result 消息 — 对齐 TS 端: message.role === 'tool-result'
            if (json.TryGetValue("role", out var roleEl) && roleEl.ValueKind == JsonValueKind.String) {
                var role = roleEl.GetString();
                if (string.Equals(role, "tool-result", StringComparison.OrdinalIgnoreCase)) return null;
            }

            // 跳过 synthetic 消息 — 对齐 TS 端: message.synthetic === true
            if (json.TryGetValue("synthetic", out var synthEl) && synthEl.ValueKind == JsonValueKind.True) return null;

            // 跳过 replay 消息 — 对齐 TS 端: message.source === 'replay'
            if (json.TryGetValue("source", out var sourceEl) && sourceEl.ValueKind == JsonValueKind.String) {
                var source = sourceEl.GetString();
                if (string.Equals(source, "replay", StringComparison.OrdinalIgnoreCase)) return null;
            }

            // 提取文本内容 — 对齐 TS 端: extractUserMessageText
            if (json.TryGetValue("content", out var contentEl) && contentEl.ValueKind == JsonValueKind.String) {
                var text = contentEl.GetString();
                return string.IsNullOrWhiteSpace(text) ? null : text;
            }

            if (json.TryGetValue("content", out var contentArrEl) && contentArrEl.ValueKind == JsonValueKind.Array) {
                // content 是数组，提取第一个 text 类型的 block
                foreach (var item in contentArrEl.EnumerateArray()) {
                    if (item.TryGetProperty("type", out var blockTypeEl) &&
                        blockTypeEl.ValueKind == JsonValueKind.String &&
                        string.Equals(blockTypeEl.GetString(), "text", StringComparison.OrdinalIgnoreCase) &&
                        item.TryGetProperty("text", out var textEl) && textEl.ValueKind == JsonValueKind.String) {
                        var text = textEl.GetString();
                        return string.IsNullOrWhiteSpace(text) ? null : text;
                    }
                }
            }

            // 兜底: 尝试 message.content（嵌套结构）
            if (json.TryGetValue("message", out var msgEl) && msgEl.ValueKind == JsonValueKind.Object) {
                if (msgEl.TryGetProperty("content", out var msgContentEl) && msgContentEl.ValueKind == JsonValueKind.String) {
                    var text = msgContentEl.GetString();
                    return string.IsNullOrWhiteSpace(text) ? null : text;
                }
            }

            return null;
        } catch {
            return null;
        }
    }

    /// <summary>
    /// 异步释放子进程资源 — 唯一释放入口：标记释放 → 终止进程 → 释放 IO 通道 → Entity 注销
    /// </summary>
    /// <returns>表示异步释放操作的 ValueTask</returns>
    public override async ValueTask DisposeAsync() {
        if (!_state.MarkDisposed()) return;

        await TerminateProcessAsync().ConfigureAwait(false);
        await _io.DisposeAsync().ConfigureAwait(false);
        await base.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>终止进程并等待退出 — 提取保持 DisposeAsync 主体清晰</summary>
    private async Task TerminateProcessAsync() {
        try {
            if (_io.IsRunning) {
                Kill();
                await _io.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "[BridgeSubprocessHandle] Dispose 时等待进程退出失败");
        }
    }
}

/// <summary>
/// 子进程生成器 — 对齐 TS 端 createSessionSpawner
/// 负责生成 jcc.exe 子进程并管理其生命周期
/// ProcessStartInfo 由 BridgeSubprocessHandle 内部创建，避免 JCC3004 分析器误报
/// </summary>
public sealed class BridgeSubprocessSpawner {
    private readonly ILogger? _logger;
    private readonly IFileSystem _fs;
    private readonly IProcessService _processService;

    /// <summary>jcc 可执行文件路径</summary>
    public string ExecPath { get; init; } = BrandConstants.CliCommandName; // P1-⑦ 委托统一数据源

    /// <summary>工作目录</summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>额外环境变量</summary>
    public Dictionary<string, string> ExtraEnv { get; init; } = [];

    /// <summary>是否调试日志</summary>
    public bool DebugLog { get; init; }

    /// <summary>关闭等待超时（毫秒）</summary>
    public int ShutdownGraceMs { get; init; } = 30000;

    /// <summary>
    /// 构造 BridgeSubprocessSpawner
    /// </summary>
    /// <param name="fs">文件系统抽象</param>
    /// <param name="processService">进程服务</param>
    /// <param name="logger">日志记录器（可选）</param>
    public BridgeSubprocessSpawner(IFileSystem fs, IProcessService processService, ILogger? logger = null) {
        _fs = fs;
        _processService = processService;
        _logger = logger;
    }

    /// <summary>
    /// 生成子进程 — 对齐 TS 端 SessionSpawner.spawn()
    /// BridgeSubprocessHandle 内部创建 ProcessStartInfo + 消费 StandardError/StandardOutput
    /// 包含 transcript 文件写入、safeFilenameId 净化、debugFile 解析
    /// </summary>
    /// <param name="options">子进程选项</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>创建好的子进程句柄</returns>
    public async Task<BridgeSubprocessHandle> SpawnAsync(BridgeSubprocessOptions options, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(options);

        // 对齐 TS 端: safeFilenameId 净化会话 ID
        var safeId = SafeFilenameId(options.SessionId);

        // 对齐 TS 端: debugFile 解析 + transcript 路径
        string? debugFile = null;
        string? transcriptPath = null;

        if (!string.IsNullOrEmpty(options.DebugFile)) {
            var extIdx = options.DebugFile.LastIndexOf('.');
            if (extIdx > 0) {
                debugFile = $"{options.DebugFile[..extIdx]}-{safeId}{options.DebugFile[extIdx..]}";
            } else {
                debugFile = $"{options.DebugFile}-{safeId}";
            }

            // 对齐 TS 端: bridge-transcript-{safeId}.json
            var debugDir = Path.GetDirectoryName(debugFile);
            transcriptPath = string.IsNullOrEmpty(debugDir)
                ? $"bridge-transcript-{safeId}.json"
                : Path.Combine(debugDir, $"bridge-transcript-{safeId}.json");
        } else if (options.DebugLog || IsAntBuild()) {
            var tempDir = JoinCode.Abstractions.Configuration.AppData.AppDataConstants.UserRuntimeDirectory;
            debugFile = Path.Combine(tempDir, $"bridge-session-{safeId}.log");
        }

        // 构建带 transcript 的路径
        var argList = BuildArgumentList(new BridgeSubprocessOptions {
            SessionId = options.SessionId,
            SdkUrl = options.SdkUrl,
            AccessToken = options.AccessToken,
            Dir = options.Dir,
            UseCcrV2 = options.UseCcrV2,
            WorkerEpoch = options.WorkerEpoch,
            PermissionMode = options.PermissionMode,
            DebugFile = debugFile,
            DebugLog = options.DebugLog,
            Sandbox = options.Sandbox,
            ScriptArgs = options.ScriptArgs,
        });

        var envVars = BuildEnvironmentVariables(options);

        var workDir = options.Dir ?? WorkingDirectory ?? _fs.GetCurrentDirectory();

        var argsDisplay = string.Join(' ', argList);
        _logger?.LogInformation("[SubprocessSpawner] 生成子进程: {ExecPath} {Args}", ExecPath, argsDisplay);

        if (!string.IsNullOrEmpty(debugFile)) {
            _logger?.LogDebug("[SubprocessSpawner] Debug log: {DebugFile}", debugFile);
        }

        // BridgeSubprocessHandle 内部创建 ProcessStartInfo + Process 并消费 StandardError/StandardOutput
        var handleOptions = new BridgeSubprocessOptions {
            SessionId = options.SessionId,
            ExecPath = ExecPath,
            ArgumentList = argList,
            EnvironmentVariables = envVars,
            Dir = workDir,
            SdkUrl = options.SdkUrl,
            AccessToken = options.AccessToken,
            UseCcrV2 = options.UseCcrV2,
            WorkerEpoch = options.WorkerEpoch,
            PermissionMode = options.PermissionMode,
            DebugFile = debugFile,
            DebugLog = options.DebugLog,
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
        if (!string.IsNullOrEmpty(transcriptPath)) {
            try {
                // 确保目录存在
                var dir = Path.GetDirectoryName(transcriptPath);
                if (!string.IsNullOrEmpty(dir) && !_fs.DirectoryExists(dir)) {
                    _fs.CreateDirectory(dir);
                }

                // FileMode.Append 在 .NET 5+ 中文件不存在时抛 FileNotFoundException
                // 需要先确保文件存在
                if (!_fs.FileExists(transcriptPath)) {
                    try {
                        await _fs.Open(transcriptPath, FileMode.CreateNew).DisposeAsync().ConfigureAwait(false);
                    } catch (IOException ex) when (_fs.FileExists(transcriptPath)) {
                        // TOCTOU 竞态：其他进程在我们检查和创建之间已创建了文件 — 安全忽略
                        _logger?.LogDebug(ex, "Transcript file already exists (created by another process): {Path}", transcriptPath);
                    }
                }

                _logger?.LogDebug("[SubprocessSpawner] Transcript log: {Path}", transcriptPath);

                // 将 transcript stream 注入 handle（handle 内部在 stdout 读取时写入）
                handle.SetTranscriptStream(new StreamWriter(_fs.Open(transcriptPath, FileMode.Append)));
            } catch (Exception ex) {
                _logger?.LogWarning(ex, "[SubprocessSpawner] Transcript 写入初始化失败（非致命）");
            }
        }

        _logger?.LogInformation("[SubprocessSpawner] 子进程已启动: SessionId={SessionId}", options.SessionId);

        return handle;
    }

    /// <summary>对齐 TS 端: safeFilenameId — 去除非法文件名字符</summary>
    /// <param name="id">原始 ID</param>
    /// <returns>净化后的安全文件名 ID</returns>
    public static string SafeFilenameId(string id) {
        return System.Text.RegularExpressions.Regex.Replace(id, @"[^a-zA-Z0-9_-]", "_");
    }

    /// <summary>检测是否为 Ant 构建</summary>
    private static bool IsAntBuild() {
        return Environment.GetEnvironmentVariable("USER_TYPE") == "ant";
    }

    /// <summary>
    /// 构建环境变量字典 — 对齐 TS 端子进程环境变量
    /// 包含 JCC_SESSION_ACCESS_TOKEN、JCC_POST_FOR_SESSION_INGRESS_V2 等
    /// </summary>
    private Dictionary<string, string> BuildEnvironmentVariables(BridgeSubprocessOptions options) {
        var env = new Dictionary<string, string>();

        // 额外环境变量
        foreach (var (key, value) in ExtraEnv) {
            env[key] = value;
        }

        // Bridge 专用环境变量 — 对齐 TS 端
        env["JCC_ENVIRONMENT_KIND"] = "bridge";
        env["JCC_AGENT_ROLE"] = "worker";

        if (options.AccessToken is not null) {
            // 对齐 TS 端: JCC_SESSION_ACCESS_TOKEN
            env[JccEnvVar.SessionAccessToken.ToValue()] = options.AccessToken;
        }

        // 剥离 bridge 的 OAuth token，子进程使用 session token
        env[JccEnvVar.OAuthToken.ToValue()] = "";

        // v1: HybridTransport (WS reads + POST writes) to Session-Ingress
        env[JccEnvVar.PostForSessionIngressV2.ToValue()] = "1";

        if (options.UseCcrV2) {
            env[JccEnvVar.BridgeUseCcrV2.ToValue()] = "1";
            if (options.WorkerEpoch.HasValue) {
                env[JccEnvVar.WorkerEpoch.ToValue()] = options.WorkerEpoch.Value.ToString();
            }
        }

        if (options.Sandbox) {
            env[JccEnvVar.ForceSandbox.ToValue()] = "1";
        }

        return env;
    }

    /// <summary>
    /// 构建命令行参数列表 — 对齐 TS 端子进程参数，使用 ArgumentList 消除字符串拼接注入风险
    /// </summary>
    private static IReadOnlyList<string> BuildArgumentList(BridgeSubprocessOptions options) {
        var args = new List<string>();

        // 额外脚本参数 — 对齐 TS 端: [...deps.scriptArgs, ...]
        if (options.ScriptArgs is not null) {
            foreach (var arg in options.ScriptArgs) {
                args.Add(arg);
            }
        }

        // --print 模式（非交互）
        args.Add(JccCliArg.Print.ToValue());

        if (options.SdkUrl is not null) {
            args.Add(JccCliArg.SdkUrl.ToValue());
            args.Add(options.SdkUrl);
        }

        if (options.SessionId is not null) {
            args.Add(JccCliArg.SessionId.ToValue());
            args.Add(options.SessionId);
        }

        args.Add(JccCliArg.InputFormat.ToValue());
        args.Add("stream-json");

        args.Add(JccCliArg.OutputFormat.ToValue());
        args.Add("stream-json");

        args.Add(JccCliArg.ReplayUserMessages.ToValue());

        if (options.DebugLog) {
            args.Add(JccCliArg.DebugLog.ToValue());
        }

        if (!string.IsNullOrEmpty(options.DebugFile)) {
            args.Add(JccCliArg.DebugFile.ToValue());
            args.Add(options.DebugFile);
        }

        if (!string.IsNullOrEmpty(options.PermissionMode)) {
            args.Add(JccCliArg.PermissionMode.ToValue());
            args.Add(options.PermissionMode);
        }

        return args;
    }

    /// <summary>
    /// 优雅关闭所有子进程 — 对齐 TS 端 runBridgeLoop 的关闭流程
    /// </summary>
    /// <param name="handles">子进程句柄列表</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    public async Task ShutdownAllAsync(
        IReadOnlyList<BridgeSubprocessHandle> handles,
        CancellationToken ct = default) {
        if (handles.Count == 0) return;

        _logger?.LogInformation("[SubprocessSpawner] 关闭 {Count} 个子进程", handles.Count);

        // 1. 向所有进程发送 SIGTERM
        foreach (var handle in handles) {
            handle.Kill();
        }

        // 2. 等待优雅退出
        try {
            var doneTasks = handles.Select(h => h.Done).ToArray();
            await Task.WhenAll(doneTasks).ConfigureAwait(false);
        } catch (OperationCanceledException) {
            // 3. 强制杀死未退出的进程
            foreach (var handle in handles.Where(h => h.IsRunning)) {
                handle.ForceKill();
            }
        }

        _logger?.LogInformation("[SubprocessSpawner] 所有子进程已关闭");
    }
}

/// <summary>
/// 子进程生成选项 — 对齐 TS 端 spawn 选项
/// </summary>
public sealed class BridgeSubprocessOptions {
    /// <summary>会话 ID</summary>
    public required string SessionId { get; init; }

    /// <summary>可执行文件路径 — 由 Spawner 填充</summary>
    public string? ExecPath { get; init; }

    /// <summary>命令行参数 — 由 Spawner 填充（回退模式，<see cref="ArgumentList"/> 优先）</summary>
    public string? Arguments { get; init; }

    /// <summary>参数化启动列表 — 由 Spawner 填充，优先于 <see cref="Arguments"/>，消除字符串拼接注入风险</summary>
    public IReadOnlyList<string> ArgumentList { get; init; } = [];

    /// <summary>环境变量 — 由 Spawner 填充</summary>
    public Dictionary<string, string> EnvironmentVariables { get; init; } = [];

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

    /// <summary>是否调试日志</summary>
    public bool DebugLog { get; init; }

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