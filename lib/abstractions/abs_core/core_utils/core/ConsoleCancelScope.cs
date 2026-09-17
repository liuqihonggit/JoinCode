namespace JoinCode.Abstractions.Utils;

/// <summary>
/// Console Ctrl+C 取消作用域 — 封装 CancelKeyPress 事件订阅/注销 + CTS 生命周期
/// <para>
/// 构造时创建 CTS + 订阅 CancelKeyPress,Dispose 时注销事件 + 释放 CTS。
/// 用 <c>using var scope = new ConsoleCancelScope(ct)</c> 管理生命周期,消除事件订阅泄漏 + CTS 泄漏。
/// </para>
/// </summary>
public sealed class ConsoleCancelScope : IDisposable
{
    private readonly CancellationTokenSource _cts;
    private readonly ConsoleCancelEventHandler _handler;
    private int _disposed;

    /// <summary>链接取消令牌 — Ctrl+C 触发取消</summary>
    public CancellationToken Token => _cts.Token;

    /// <summary>
    /// 创建取消作用域,链接外部令牌 <paramref name="ct"/>,并订阅 Console.CancelKeyPress。
    /// </summary>
    /// <param name="ct">外部取消令牌,与 Ctrl+C 共同触发取消</param>
    public ConsoleCancelScope(CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _handler = (_, e) => { e.Cancel = true; _cts.Cancel(); };
        System.Console.CancelKeyPress += _handler;
    }

    /// <summary>
    /// 注销 CancelKeyPress 事件 + 释放 CTS。幂等,多次调用安全。
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        System.Console.CancelKeyPress -= _handler;
        _cts.Dispose();
    }
}
