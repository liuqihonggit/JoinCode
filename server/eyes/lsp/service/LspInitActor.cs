namespace Services.Lsp.Internal;

/// <summary>
/// LspManager Actor 命令 — Channel 中的消息类型
/// </summary>
internal interface ILspCommand;

/// <summary>初始化命令 — 携带配置列表、取消令牌和完成源</summary>
internal sealed record InitializeCmd(List<LspInstanceConfig> Configs, CancellationToken Ct, TaskCompletionSource Tcs) : ILspCommand;

/// <summary>关闭命令 — 携带取消令牌和完成源</summary>
internal sealed record ShutdownCmd(CancellationToken Ct, TaskCompletionSource Tcs) : ILspCommand;

/// <summary>
/// LSP 初始化 Actor — 序列化 Initialize/Shutdown，消除 AsyncLock 锁内长 await（Shutdown 停止所有 LSP 服务器 >5s）。
/// </summary>
internal sealed class LspInitActor : ActorBase<ILspCommand, Unit>
{
    private readonly LspManager _owner;
    private readonly ILogger<LspManager> _logger;

    /// <summary>
    /// 构造 LSP 初始化 Actor
    /// </summary>
    /// <param name="owner">所属的 LSP 管理器</param>
    /// <param name="logger">日志记录器</param>
    public LspInitActor(LspManager owner, ILogger<LspManager> logger)
        : base()
    {
        _owner = owner;
        _logger = logger;
    }

    private static TaskCompletionSource CreateTcs() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// 异步初始化 — 通过 Actor 邮箱序列化 InitializeCmd 执行
    /// </summary>
    /// <param name="configs">LSP 实例配置列表</param>
    /// <param name="ct">取消令牌</param>
    public async Task InitializeAsync(List<LspInstanceConfig> configs, CancellationToken ct)
    {
        var tcs = CreateTcs();
        await SendAsync(new InitializeCmd(configs, ct, tcs), ct).ConfigureAwait(false);
        await tcs.Task.ConfigureAwait(false);
    }

    /// <summary>
    /// 异步关闭 — 通过 Actor 邮箱序列化 ShutdownCmd 执行
    /// </summary>
    /// <param name="ct">取消令牌</param>
    public async Task ShutdownAsync(CancellationToken ct)
    {
        var tcs = CreateTcs();
        await SendAsync(new ShutdownCmd(ct, tcs), ct).ConfigureAwait(false);
        await tcs.Task.ConfigureAwait(false);
    }

    /// <summary>
    /// 处理 Actor 命令 — 分发到 InitializeCoreAsync 或 ShutdownCoreAsync
    /// </summary>
    /// <param name="command">待处理的 LSP 命令</param>
    /// <param name="ct">取消令牌</param>
    protected override async ValueTask HandleAsync(ILspCommand command, CancellationToken ct)
    {
        switch (command)
        {
            case InitializeCmd cmd:
            {
                try
                {
                    await _owner.InitializeCoreAsync(cmd.Configs, cmd.Ct).ConfigureAwait(false);
                    cmd.Tcs.TrySetResult();
                }
                catch (Exception ex) { cmd.Tcs.TrySetException(ex); }
                break;
            }
            case ShutdownCmd cmd:
            {
                try
                {
                    await _owner.ShutdownCoreAsync(cmd.Ct).ConfigureAwait(false);
                    cmd.Tcs.TrySetResult();
                }
                catch (Exception ex) { cmd.Tcs.TrySetException(ex); }
                break;
            }
        }
    }

    /// <summary>
    /// 消费者异常钩子 — 记录 Actor 消费循环中的未处理异常
    /// </summary>
    /// <param name="ex">捕获的异常</param>
    protected override void OnConsumerError(Exception ex)
    {
        _logger.LogError(ex, "[LspManager] Init Actor Consumer 异常");
    }
}
