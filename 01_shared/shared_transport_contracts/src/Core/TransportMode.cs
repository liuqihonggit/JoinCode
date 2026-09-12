namespace JoinCode.Transport;

/// <summary>
/// Agent 传输模式 — 用于 DI 一键切换传输实现
/// </summary>
public enum TransportMode
{
    /// <summary>标准输入输出（子进程模式）</summary>
    Stdio,
    /// <summary>SSE 服务端推送（远程模式）</summary>
    Sse,
}
