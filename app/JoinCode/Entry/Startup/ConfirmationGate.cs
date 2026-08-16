namespace JoinCode;

/// <summary>
/// 确认门控接口 — 协调 readTask 和 CliPermissionConfirmationHandler 的输入路由。
/// 当确认待处理时，readTask 将输入路由到 Source 而非 inputChannel。
/// </summary>
public interface IConfirmationGate
{
    /// <summary>是否有确认待处理。</summary>
    bool Pending { get; }

    /// <summary>当前待处理的确认源（若有）。</summary>
    TaskCompletionSource<string>? Source { get; }

    /// <summary>设置待确认状态。</summary>
    void SetPending(TaskCompletionSource<string> source);

    /// <summary>清除待确认状态。</summary>
    void Clear();
}

/// <summary>
/// 确认门控实例 — 消除静态字段串扰，支持多会话隔离。
/// 注册为 Singleton：当前单会话 CLI 够用；多会话场景改为 Scoped + 会话级 scope。
/// </summary>
[Register(typeof(IConfirmationGate))]
internal sealed class ConfirmationGate : IConfirmationGate
{
    private volatile bool _pending;
    private TaskCompletionSource<string>? _source;

    /// <inheritdoc />
    public bool Pending => _pending;

    /// <inheritdoc />
    public TaskCompletionSource<string>? Source => _source;

    /// <inheritdoc />
    public void SetPending(TaskCompletionSource<string> source)
    {
        _source = source;
        _pending = true;
    }

    /// <inheritdoc />
    public void Clear()
    {
        _pending = false;
        _source = null;
    }
}
