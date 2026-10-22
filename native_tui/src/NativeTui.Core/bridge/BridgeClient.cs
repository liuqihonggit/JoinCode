namespace NativeTui.Core.Bridge;

/// <summary>
/// bridge 引擎客户端 — TUI 父进程封装（ADR 0141）：拉起引擎子进程（stdio 管道），
/// 请求写 stdin（Submit/Interrupt/SetPermissionMode/StopBackgroundTask），事件由 BridgeEventPump
/// 后台排空（DrainEvents）。Dispose：发 quit → 关 stdin → 等引擎退出（无超时 — 关闭即释放）→ 等泵收尾。
/// 引擎 stderr 直通终端（不重定向）：诊断可见且无管道缓冲填满死锁风险。
/// </summary>
public sealed class BridgeClient : IDisposable
{
    private readonly TextWriter _stdin;
    private readonly Process? _process;
    private readonly BridgeEventPump _pump;

    private BridgeClient(Process process, BridgeEventPump pump)
    {
        _process = process;
        _stdin = process.StandardInput;
        _pump = pump;
    }

    /// <summary>测试构造 — 注入 stdin 写面与 stdout 读面（internal — BridgeClientTests 单测协议行；
    /// 无子进程，Dispose 只收事件泵；对齐生产构造的泵启动形态）。</summary>
    internal BridgeClient(TextWriter stdin, TextReader stdout)
    {
        _process = null;
        _stdin = stdin;
        _pump = new BridgeEventPump(stdout);
        _pump.Start();
    }

    /// <summary>引擎进程是否仍存活（测试构造无子进程恒活 — 状态由事件泵 EOF 感知）。</summary>
    public bool IsEngineAlive => _process is null || !_process.HasExited;

    /// <summary>拉起引擎子进程并启动事件泵（exe 不存在 / 启动失败抛异常）。</summary>
    public static BridgeClient Start(string exePath, string arguments = "")
    {
        var psi = new ProcessStartInfo(exePath, arguments)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = false,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
        };
        var process = Process.Start(psi) ?? throw new InvalidOperationException($"引擎进程启动失败：{exePath}");
        var pump = new BridgeEventPump(process.StandardOutput);
        pump.Start();
        return new BridgeClient(process, pump);
    }

    /// <summary>提交用户输入（开始回合）。</summary>
    public void Submit(string text) =>
        BridgeProtocol.WriteRequest(_stdin, new BridgeRequest.Submit(text));

    /// <summary>打断当前回合。</summary>
    public void Interrupt() =>
        BridgeProtocol.WriteRequest(_stdin, new BridgeRequest.Interrupt());

    /// <summary>切换权限模式（mode 为协议值 plan/auto/ask/bypass/unattended；生效值以引擎回执事件为准）。</summary>
    public void SetPermissionMode(string mode) =>
        BridgeProtocol.WriteRequest(_stdin, new BridgeRequest.SetPermissionMode(mode));

    /// <summary>停止后台任务（M8-8 — kind: fork|teammate，写 stdin stop_task 请求；
    /// 即发即返不等回执 — 引擎侧 ServeLoop 分派 CancelFork/StopTeammate 后回推 cancelled 终态
    /// background_task 事件，面板即时反馈走事件链，对齐 Interrupt 的发送形态）。</summary>
    /// <param name="kind">任务类别（fork|teammate）</param>
    /// <param name="taskId">任务标识</param>
    public void StopBackgroundTask(string kind, string taskId) =>
        BridgeProtocol.WriteRequest(_stdin, new BridgeRequest.StopTask(kind, taskId));

    /// <summary>排空事件队列（返回本次取出的全部事件）。</summary>
    public IReadOnlyList<BridgeEvent> DrainEvents() => _pump.DrainEvents();

    /// <summary>排空诊断队列（未知 type / 坏 JSON / 流异常，调用方记 stderr）。</summary>
    public IReadOnlyList<string> DrainDiagnostics() => _pump.DrainDiagnostics();

    /// <summary>退出会话：quit → 关 stdin → 等引擎退出 → 等泵收尾 → 释放进程句柄。</summary>
    public void Dispose()
    {
        CloseEngineGracefully();
        _process?.WaitForExit();
        _pump.Dispose();
        _process?.Dispose();
    }

    /// <summary>优雅关闭：发 quit + 关 stdin（引擎已退出/管道断开时跳过 — 尽力而为）。</summary>
    private void CloseEngineGracefully()
    {
        try
        {
            BridgeProtocol.WriteRequest(_stdin, new BridgeRequest.Quit());
            _stdin.Close();
        }
        catch (IOException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }
}
