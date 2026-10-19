namespace NativeTui.Core.Bridge;

/// <summary>
/// bridge 引擎客户端 — TUI 父进程封装（ADR 0141）：拉起引擎子进程（stdio 管道），
/// 请求写 stdin（Submit/Interrupt），事件由 BridgeEventPump 后台排空（DrainEvents）。
/// Dispose：发 quit → 关 stdin → 等引擎退出（无超时 — 关闭即释放）→ 等泵收尾。
/// 引擎 stderr 直通终端（不重定向）：诊断可见且无管道缓冲填满死锁风险。
/// </summary>
public sealed class BridgeClient : IDisposable
{
    private readonly Process _process;
    private readonly BridgeEventPump _pump;

    private BridgeClient(Process process, BridgeEventPump pump)
    {
        _process = process;
        _pump = pump;
    }

    /// <summary>引擎进程是否仍存活。</summary>
    public bool IsEngineAlive => !_process.HasExited;

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
        BridgeProtocol.WriteRequest(_process.StandardInput, new BridgeRequest.Submit(text));

    /// <summary>打断当前回合。</summary>
    public void Interrupt() =>
        BridgeProtocol.WriteRequest(_process.StandardInput, new BridgeRequest.Interrupt());

    /// <summary>排空事件队列（返回本次取出的全部事件）。</summary>
    public IReadOnlyList<BridgeEvent> DrainEvents() => _pump.DrainEvents();

    /// <summary>排空诊断队列（未知 type / 坏 JSON / 流异常，调用方记 stderr）。</summary>
    public IReadOnlyList<string> DrainDiagnostics() => _pump.DrainDiagnostics();

    /// <summary>退出会话：quit → 关 stdin → 等引擎退出 → 等泵收尾 → 释放进程句柄。</summary>
    public void Dispose()
    {
        CloseEngineGracefully();
        _process.WaitForExit();
        _pump.Dispose();
        _process.Dispose();
    }

    /// <summary>优雅关闭：发 quit + 关 stdin（引擎已退出/管道断开时跳过 — 尽力而为）。</summary>
    private void CloseEngineGracefully()
    {
        try
        {
            BridgeProtocol.WriteRequest(_process.StandardInput, new BridgeRequest.Quit());
            _process.StandardInput.Close();
        }
        catch (IOException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }
}
