namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 进程执行结果
/// </summary>
public sealed class ProcessResult : ICommandExecutionResult {
    /// <summary>获取进程退出码。</summary>
    public required int ExitCode { get; init; }
    /// <summary>获取标准输出内容。</summary>
    public required string StandardOutput { get; init; }
    /// <summary>获取标准错误内容。</summary>
    public required string StandardError { get; init; }
    /// <summary>获取执行时长。</summary>
    public required TimeSpan ExecutionTime { get; init; }
    /// <summary>获取进程是否执行成功。</summary>
    public bool Success => ExitCode == 0;

    int? ICommandExecutionResult.ExitCode => ExitCode;
    string ICommandExecutionResult.Output => StandardOutput;
    string ICommandExecutionResult.Error => StandardError;

    /// <summary>返回结果摘要字符串。</summary>
    public override string ToString() =>
        $"[Process {(Success ? "OK" : "FAIL")}] ExitCode={ExitCode}, {ExecutionTime.TotalMilliseconds:F0}ms";
}

/// <summary>
/// 进程启动选项
/// </summary>
public sealed class ProcessOptions {
    /// <summary>获取或设置可执行文件名。</summary>
    public required string FileName { get; init; }
    /// <summary>获取或设置命令行参数字符串。</summary>
    public string Arguments { get; init; } = string.Empty;
    /// <summary>
    /// 参数化启动列表 — 优先于 <see cref="Arguments"/>，通过 ProcessStartInfo.ArgumentList 逐个添加，消除字符串拼接注入风险
    /// <para>非空时忽略 <see cref="Arguments"/>；为空列表时回退到 <see cref="Arguments"/></para>
    /// </summary>
    public IReadOnlyList<string> ArgumentList { get; init; } = [];
    /// <summary>获取或设置工作目录。</summary>
    public string? WorkingDirectory { get; init; }
    /// <summary>获取或设置环境变量字典。</summary>
    public IReadOnlyDictionary<string, string> EnvironmentVariables { get; init; } = new Dictionary<string, string>();
    /// <summary>获取或设置标准输出编码。</summary>
    public System.Text.Encoding? StandardOutputEncoding { get; init; }
    /// <summary>获取或设置标准错误编码。</summary>
    public System.Text.Encoding? StandardErrorEncoding { get; init; }
    /// <summary>获取或设置超时时间（毫秒）。</summary>
    public int? TimeoutMs { get; init; }
    /// <summary>获取或设置是否重定向标准输出。</summary>
    public bool RedirectStandardOutput { get; init; } = true;
    /// <summary>获取或设置是否重定向标准错误。</summary>
    public bool RedirectStandardError { get; init; } = true;
    /// <summary>
    /// 是否跳过参数危险字符校验 — 仅用于 Shell 命令本身需要元字符的场景（如 bash -c "cmd &amp;&amp; cmd"）
    /// <para>默认 false：执行参数黑名单校验，拒绝 &amp;|;`$()&lt;&gt; 等 shell 元字符</para>
    /// </summary>
    public bool SkipArgumentValidation { get; init; }
}

/// <summary>
/// 交互式进程句柄 — 用于需要持续读写 stdin/stdout 的场景（MCP Stdio、插件宿主等）
/// </summary>
public interface IInteractiveProcess : IAsyncDisposable {
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
public sealed class InteractiveProcessOptions {
    /// <summary>获取或设置可执行文件名。</summary>
    public required string FileName { get; init; }
    /// <summary>获取或设置命令行参数字符串。</summary>
    public string Arguments { get; init; } = string.Empty;
    /// <summary>
    /// 参数化启动列表 — 优先于 <see cref="Arguments"/>，消除字符串拼接注入风险
    /// <para>非空时忽略 <see cref="Arguments"/>；为空列表时回退到 <see cref="Arguments"/></para>
    /// </summary>
    public IReadOnlyList<string> ArgumentList { get; init; } = [];
    /// <summary>获取或设置工作目录。</summary>
    public string? WorkingDirectory { get; init; }
    /// <summary>获取或设置环境变量字典。</summary>
    public IReadOnlyDictionary<string, string> EnvironmentVariables { get; init; } = new Dictionary<string, string>();
    /// <summary>获取或设置是否重定向标准错误。</summary>
    public bool RedirectStandardError { get; init; } = true;
    /// <summary>获取或设置标准输出编码。</summary>
    public System.Text.Encoding? StandardOutputEncoding { get; init; }
    /// <summary>获取或设置标准错误编码。</summary>
    public System.Text.Encoding? StandardErrorEncoding { get; init; }
    /// <summary>获取或设置标准输入编码。</summary>
    public System.Text.Encoding? StandardInputEncoding { get; init; }
    /// <summary>
    /// 是否跳过参数危险字符校验 — 仅用于 Shell 命令本身需要元字符的场景
    /// </summary>
    public bool SkipArgumentValidation { get; init; }
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
public interface IProcessService {
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
