namespace Core.Utils;

/// <summary>
/// 传输拓扑类型 — 邮箱跨进程通信的网络结构。
/// </summary>
public enum TransportTopology
{
    /// <summary>星型拓扑 — 主机中心转发，从机连主机。先实现。</summary>
    [EnumValue("star")] Star,

    /// <summary>网状拓扑 — 每进程一个管道，点对点直连。后续。</summary>
    [EnumValue("mesh")] Mesh,

    /// <summary>总线拓扑 — 共享服务器，多客户端连同一管道。后续。</summary>
    [EnumValue("bus")] Bus,
}

/// <summary>
/// 进程角色 — 主机/从机。
/// </summary>
public enum ProcessRole
{
    /// <summary>主机 — 传输层服务器，接收所有从机连接，转发消息。</summary>
    [EnumValue("host")] Host,

    /// <summary>从机 — 传输层客户端，连接主机，通过主机转发消息。</summary>
    [EnumValue("slave")] Slave,
}

/// <summary>
/// 传输帧 — 跨进程传输的数据单元，含源进程标识和负载数据。
/// </summary>
/// <param name="SourceProcessId">源进程标识</param>
/// <param name="Data">负载数据（已序列化的字节）</param>
public sealed record TransportFrame(string SourceProcessId, ReadOnlyMemory<byte> Data);

/// <summary>
/// 传输拓扑接口 — 邮箱跨进程通信的抽象传输层，拓扑可插拔替换。
/// <para>实现：星型（<see cref="TransportTopology.Star"/>）、网状（<see cref="TransportTopology.Mesh"/>）、总线（<see cref="TransportTopology.Bus"/>）。</para>
/// <para>序列化由调用方（如 <c>NamedPipeMailbox</c>）负责，本接口只传输 <see cref="ReadOnlyMemory{T}"/> 字节。</para>
/// <para>主机角色由 <see cref="HostElectionService"/> 选举决定，本接口只负责按角色建立传输管道。</para>
/// </summary>
public interface ITransportTopology : IAsyncDisposable
{
    /// <summary>拓扑类型。</summary>
    TransportTopology Kind { get; }

    /// <summary>当前进程角色（主机/从机），由选举决定。</summary>
    ProcessRole Role { get; }

    /// <summary>当前进程标识（PID 或句柄字符串）。</summary>
    string ProcessId { get; }

    /// <summary>主机进程标识；从机模式下为主机 PID，主机模式下为自身 PID。</summary>
    string HostProcessId { get; }

    /// <summary>传输层是否已启动。</summary>
    bool IsRunning { get; }

    /// <summary>
    /// 启动传输层 — 主机创建服务器管道，从机连接主机管道。
    /// <para>主机角色由 <see cref="HostElectionService"/> 探测决定。</para>
    /// </summary>
    /// <param name="ct">取消令牌</param>
    ValueTask StartAsync(CancellationToken ct = default);

    /// <summary>
    /// 跨进程发送数据到指定目标进程。
    /// <para>星型拓扑：从机→主机→目标进程；主机→直接转发到目标进程。</para>
    /// <para>目标进程不存在时静默丢弃（best-effort）。</para>
    /// </summary>
    /// <param name="targetProcessId">目标进程标识</param>
    /// <param name="data">负载数据</param>
    /// <param name="ct">取消令牌</param>
    ValueTask SendAsync(string targetProcessId, ReadOnlyMemory<byte> data, CancellationToken ct = default);

    /// <summary>
    /// 跨进程广播数据到所有已连接进程。
    /// </summary>
    /// <param name="data">负载数据</param>
    /// <param name="ct">取消令牌</param>
    ValueTask BroadcastAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default);

    /// <summary>
    /// 接收跨进程消息流 — 阻塞式 IAsyncEnumerable，传输层运行期间持续产出。
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>传输帧异步流</returns>
    IAsyncEnumerable<TransportFrame> ReceiveAsync(CancellationToken ct = default);

    /// <summary>获取所有已连接的远程进程标识。</summary>
    IReadOnlyCollection<string> GetConnectedProcesses();
}

/// <summary>
/// 主机上下文快照 — 主机定期同步给所有从机，用于故障转移。
/// <para>完整上下文：路由表 + 未投递消息 + 编译队列状态。</para>
/// </summary>
public sealed record HostContextSnapshot
{
    /// <summary>快照时间戳。</summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>主机进程标识。</summary>
    public required string HostProcessId { get; init; }

    /// <summary>路由表 — AgentId → ProcessId 映射。</summary>
    public required IReadOnlyDictionary<string, string> RoutingTable { get; init; }

    /// <summary>未投递消息 — AgentId → 待投递消息字节列表（序列化后）。</summary>
    public required IReadOnlyDictionary<string, IReadOnlyList<ReadOnlyMemory<byte>>> PendingMessages { get; init; }

    /// <summary>全局编译队列状态 — 排队中 + 执行中的编译任务。</summary>
    public required BuildQueueState BuildQueue { get; init; }
}

/// <summary>
/// 编译队列状态 — 全局编译队列的快照。
/// </summary>
public sealed record BuildQueueState
{
    /// <summary>排队中的编译任务数。</summary>
    public int PendingCount { get; init; }

    /// <summary>正在执行的编译任务数（全局串行，最多 1）。</summary>
    public int RunningCount { get; init; }

    /// <summary>排队中的任务摘要（进程 ID + 任务 ID）。</summary>
    public required IReadOnlyList<string> PendingTasks { get; init; }
}

/// <summary>
/// 管道接受循环辅助 — 封装 NamedPipeServerStream 接受连接的循环+协商式取消,
/// 消除三个 Transport 的重复代码。取消时通过 ct.Register Dispose server 强制中断 WaitForConnectionAsync。
/// </summary>
internal static class PipeAcceptLoop
{
    /// <summary>
    /// 循环接受管道连接,每接受一个连接调 handleConnection 处理。取消时优雅退出。
    /// </summary>
    public static async Task RunAsync(
        string pipeName,
        Func<NamedPipeServerStream, CancellationToken, Task> handleConnection,
        CancellationToken ct)
    {
        const int PipeBufferSize = 65536;
        while (!ct.IsCancellationRequested)
        {
            var server = new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous,
                inBufferSize: PipeBufferSize,
                outBufferSize: PipeBufferSize);
            try
            {
                using var reg = ct.Register(static s => ((NamedPipeServerStream)s!).Dispose(), server);
                await server.WaitForConnectionAsync(ct).ConfigureAwait(false);
                reg.Unregister();
                _ = Task.Run(() => handleConnection(server, ct), ct);
            }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[PIPE-ACCEPT] 连接错误: {ex.Message}");
                try { server.Dispose(); }
                catch (Exception) { Console.Error.WriteLine("[PIPE-ACCEPT] server Dispose 失败"); }
            }
        }
    }
}

/// <summary>
/// 任务等待辅助 — 封装 DisposeAsync 中 await 后台任务带超时的模式,消除重复 try-catch。
/// </summary>
internal static class TaskAwaitHelper
{
    /// <summary>
    /// 等待任务完成,超时或取消时静默返回(不抛异常)。
    /// </summary>
    public static async ValueTask AwaitWithTimeout(Task? task, TimeSpan timeout)
    {
        if (task is null) return;
        try { await task.WaitAsync(timeout).ConfigureAwait(false); }
        catch (TimeoutException) { Console.Error.WriteLine($"[TASK-AWAIT] 等待任务超时 {timeout.TotalSeconds:F1}s,放弃等待"); }
        catch (OperationCanceledException) { }
    }
}
