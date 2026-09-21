namespace Core.Utils;

/// <summary>
/// 有名管道创建工厂 — 全项目管道创建的唯一入口。
/// <para>集中管控缓冲区大小、管道选项、传输模式等配置,禁止在业务代码中直接 <c>new NamedPipeServerStream</c> / <c>new NamedPipeClientStream</c>。</para>
/// <para><b>历史教训</b>: 曾因各处直接 new NamedPipeServerStream 使用5参数构造函数,默认缓冲区大小为0导致写操作永久阻塞(见 docs/task/管道缓冲区零导致写阻塞-bug修复记录.md)。</para>
/// <para>本工厂强制指定 <see cref="PipeBufferSize"/>(65536)字节缓冲区,从源头消除该类问题。</para>
/// </summary>
public static class NamedPipeFactory {
    /// <summary>
    /// 管道缓冲区大小 — 65536 字节(64KB)。
    /// <para>足够容纳多条消息,避免写操作因缓冲区满而阻塞。</para>
    /// <para>历史根因: �" 缓冲区大小为0,写操作阻塞直到对端读取,导致死锁。</para>
    /// </summary>
    public const int PipeBufferSize = 65536;

    /// <summary>
    /// 管道选项 — Asynchronous,支持异步读写。
    /// </summary>
    public const PipeOptions PipeOpt = PipeOptions.Asynchronous;

    /// <summary>
    /// 创建有名管道服务端 — 强制指定64KB缓冲区,双向通信,异步模式。
    /// <para>使用 <see cref="NamedPipeServerStream.MaxAllowedServerInstances"/> 允许最大并发实例数。</para>
    /// </summary>
    /// <param name="pipeName">管道名称</param>
    /// <returns>配置好的 <see cref="NamedPipeServerStream"/>,调用方负责 Dispose</returns>
    public static NamedPipeServerStream CreateServer(string pipeName) {
        TransportDiagnostics.Log("PIPE-FACTORY", () => $"CreateServer: {pipeName}, buf={PipeBufferSize}");
        return new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOpt,
            inBufferSize: PipeBufferSize,
            outBufferSize: PipeBufferSize);
    }

    /// <summary>
    /// 创建有名管道客户端 — 双向通信,异步模式。
    /// <para>客户端缓冲区大小由服务端决定,无需指定。</para>
    /// <para>服务器名固定为 "."(本机),跨机器通信不适用有名管道。</para>
    /// </summary>
    /// <param name="pipeName">管道名称</param>
    /// <returns>配置好的 <see cref="NamedPipeClientStream"/>,调用方负责 Dispose</returns>
    public static NamedPipeClientStream CreateClient(string pipeName) {
        TransportDiagnostics.Log("PIPE-FACTORY", () => $"CreateClient: {pipeName}");
        return new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOpt);
    }
}