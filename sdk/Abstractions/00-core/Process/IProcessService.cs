namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 进程执行结果
/// </summary>
public sealed class ProcessResult
{
    public required int ExitCode { get; init; }
    public required string StandardOutput { get; init; }
    public required string StandardError { get; init; }
    public required TimeSpan ExecutionTime { get; init; }
    public bool Success => ExitCode == 0;
}

/// <summary>
/// 进程启动选项
/// </summary>
public sealed class ProcessOptions
{
    public required string FileName { get; init; }
    public string Arguments { get; init; } = string.Empty;
    public string? WorkingDirectory { get; init; }
    public IReadOnlyDictionary<string, string>? EnvironmentVariables { get; init; }
    public System.Text.Encoding? StandardOutputEncoding { get; init; }
    public System.Text.Encoding? StandardErrorEncoding { get; init; }
    public int? TimeoutMs { get; init; }
    public bool RedirectStandardOutput { get; init; } = true;
    public bool RedirectStandardError { get; init; } = true;
}

/// <summary>
/// 交互式进程句柄 — 用于需要持续读写 stdin/stdout 的场景（MCP Stdio、插件宿主等）
/// </summary>
public interface IInteractiveProcess : IAsyncDisposable
{
    /// <summary>进程标准输入写入器</summary>
    System.IO.StreamWriter StandardInput { get; }

    /// <summary>进程标准输出读取器</summary>
    System.IO.StreamReader StandardOutput { get; }

    /// <summary>进程 ID</summary>
    int Id { get; }

    /// <summary>进程是否已退出</summary>
    bool HasExited { get; }

    /// <summary>进程退出码</summary>
    int ExitCode { get; }

    /// <summary>等待进程退出</summary>
    Task WaitForExitAsync(CancellationToken ct = default);

    /// <summary>终止进程</summary>
    void Kill();

    /// <summary>错误输出事件</summary>
    event EventHandler<string>? ErrorDataReceived;
}

/// <summary>
/// 交互式进程启动选项
/// </summary>
public sealed class InteractiveProcessOptions
{
    public required string FileName { get; init; }
    public string Arguments { get; init; } = string.Empty;
    public string? WorkingDirectory { get; init; }
    public IReadOnlyDictionary<string, string>? EnvironmentVariables { get; init; }
    public bool RedirectStandardError { get; init; } = true;
    public System.Text.Encoding? StandardOutputEncoding { get; init; }
    public System.Text.Encoding? StandardErrorEncoding { get; init; }
    public System.Text.Encoding? StandardInputEncoding { get; init; }
}

/// <summary>
/// 进程服务接口 — 抽象 System.Diagnostics.Process 操作
/// <para>
/// 核心价值：
/// 1. 消除 Process 死锁风险（内部强制先读 stdout/stderr 再 WaitForExit）
/// 2. 支持测试替身（NoOp 模式：JCC_PROCESS_MODE=NoOp）
/// 3. 集中审计和度量
/// </para>
/// <para>生产环境: PhysicalProcessService (委托给 System.Diagnostics.Process)</para>
/// <para>测试环境: NoOpProcessService (跳过所有进程操作)</para>
/// </summary>
public interface IProcessService
{
    /// <summary>
    /// 执行命令并等待退出 — 覆盖简单执行模式
    /// <para>内部自动处理 stdout/stderr 读取顺序，消除死锁风险</para>
    /// </summary>
    Task<ProcessResult> ExecuteAsync(ProcessOptions options, CancellationToken ct = default);

    /// <summary>
    /// 启动交互式进程 — 覆盖流式交互模式
    /// <para>返回 IInteractiveProcess 句柄，调用方通过 StandardInput/StandardOutput 持续通信</para>
    /// </summary>
    Task<IInteractiveProcess> StartInteractiveAsync(InteractiveProcessOptions options, CancellationToken ct = default);

    /// <summary>
    /// 打开 URL / 文件 / 目录 — 覆盖启动即忘模式
    /// <para>内部使用 UseShellExecute=true，NoOp 模式自动跳过</para>
    /// </summary>
    Task<bool> OpenAsync(string path, CancellationToken ct = default);

    /// <summary>
    /// 查找可执行文件路径 — 覆盖可用性检测模式
    /// <para>内部使用 where(Windows) / which(Unix) 命令</para>
    /// </summary>
    Task<string?> FindExecutableAsync(string name, CancellationToken ct = default);

    /// <summary>
    /// 检查指定名称的进程是否正在运行
    /// </summary>
    bool IsProcessRunning(string processName);
}
