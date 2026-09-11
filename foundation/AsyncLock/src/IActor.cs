namespace Core.Utils;

/// <summary>
/// Actor 最小接口 — 提供类型安全的消息发送能力，不关心具体 Actor 类型和输出类型。
/// <para>用于解耦调用方与具体 Actor 实现：调用方持有 <c>IActor&lt;TCommand&gt;</c> 即可发消息，无需知道 TOut 或具体类型。</para>
/// <para>大规模 Actor 改造时，此接口是"发消息不关心具体 Actor 类型"的基础设施。</para>
/// </summary>
/// <typeparam name="TCommand">命令类型 — 建议用 record 或 sealed class</typeparam>
public interface IActor<TCommand> : IAsyncDisposable
{
    /// <summary>Actor 唯一标识 — 用于日志和监控</summary>
    string Id { get; }

    /// <summary>当前输入邮箱消息数 — 用于监控背压状态</summary>
    int InputCount { get; }

    /// <summary>
    /// 异步发送命令 — 无界通道立即返回，有界通道满时背压等待。
    /// <para>配置了发送超时时，超时抛 <see cref="TimeoutException"/>。</para>
    /// </summary>
    /// <param name="cmd">命令实例</param>
    /// <param name="ct">取消令牌</param>
    /// <exception cref="ObjectDisposedException">Actor 已释放</exception>
    /// <exception cref="TimeoutException">发送超时（背压配置了 SendTimeout 且通道满）</exception>
    ValueTask SendAsync(TCommand cmd, CancellationToken ct = default);

    /// <summary>
    /// 同步尝试发送命令 — 通道已关闭、已释放或（有界通道）已满时返回 false。
    /// </summary>
    /// <param name="cmd">命令实例</param>
    /// <returns>true 表示已入队，false 表示未入队</returns>
    bool TrySend(TCommand cmd);
}
