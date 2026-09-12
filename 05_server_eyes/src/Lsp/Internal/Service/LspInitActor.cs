namespace Services.Lsp.Internal;

/// <summary>
/// LspManager Actor 命令 — Channel 中的消息类型
/// </summary>
internal interface ILspCommand;

internal sealed record InitializeCmd(List<LspInstanceConfig> Configs, CancellationToken Ct, TaskCompletionSource Tcs) : ILspCommand;
internal sealed record ShutdownCmd(CancellationToken Ct, TaskCompletionSource Tcs) : ILspCommand;

/// <summary>
/// LSP 初始化 Actor — 序列化 Initialize/Shutdown，消除 AsyncLock 锁内长 await（Shutdown 停止所有 LSP 服务器 >5s）。
/// </summary>
internal sealed class LspInitActor : ActorBase<ILspCommand, Unit>
{
    private readonly LspManager _owner;
    private readonly ILogger<LspManager> _logger;

    public LspInitActor(LspManager owner, ILogger<LspManager> logger)
        : base()
    {
        _owner = owner;
        _logger = logger;
    }

    private static TaskCompletionSource CreateTcs() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task InitializeAsync(List<LspInstanceConfig> configs, CancellationToken ct)
    {
        var tcs = CreateTcs();
        await SendAsync(new InitializeCmd(configs, ct, tcs), ct).ConfigureAwait(false);
        await tcs.Task.ConfigureAwait(false);
    }

    public async Task ShutdownAsync(CancellationToken ct)
    {
        var tcs = CreateTcs();
        await SendAsync(new ShutdownCmd(ct, tcs), ct).ConfigureAwait(false);
        await tcs.Task.ConfigureAwait(false);
    }

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

    protected override void OnConsumerError(Exception ex)
    {
        _logger.LogError(ex, "[LspManager] Init Actor Consumer 异常");
    }
}
