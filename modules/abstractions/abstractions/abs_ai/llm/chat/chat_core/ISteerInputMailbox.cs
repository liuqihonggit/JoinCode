namespace JoinCode.Abstractions.LLM.Chat;

/// <summary>
/// 轮内注入邮箱（Actor 邮箱模型）— 轮次运行期间把用户输入投递给**当前轮的下一个 step**，
/// 而不是排队等下一轮。对齐 DSH <c>agent.steer(input)</c> → <c>send(input,'next-step',true)</c>
/// （<c>dsh-agent-loop/lib/index.js:809</c>）与「本步无工具调用但 next-step 非空 → 不结束轮次」
/// （<c>:998-1006</c>）。
/// </summary>
/// <remarks>
/// 按会话寻址：宿主只会往「该会话」的邮箱投递，引擎在每步开头 drain 本会话，
/// 因此子代理/其它会话的注入不会串味。实现必须是线程安全的（宿主线程投递、引擎线程取件）。
/// </remarks>
public interface ISteerInputMailbox {
    /// <summary>把一段用户输入投递到该会话的注入邮箱（空文本忽略）。</summary>
    /// <param name="sessionId">目标会话 ID</param>
    /// <param name="text">用户输入文本</param>
    void Enqueue(string sessionId, string text);

    /// <summary>该会话是否有待注入的输入（步末「不结束轮次」判断用）。</summary>
    /// <param name="sessionId">目标会话 ID</param>
    /// <returns>有待注入返回 true</returns>
    bool HasPending(string sessionId);

    /// <summary>取走该会话全部待注入输入（FIFO 顺序，取走后邮箱清空）。</summary>
    /// <param name="sessionId">目标会话 ID</param>
    /// <returns>按投递顺序排列的输入文本；无待注入返回空集合</returns>
    IReadOnlyList<string> DrainAll(string sessionId);
}
