namespace NativeTui.Core.Repl;

/// <summary>
/// 输入请求种类（S8）— 命令交互回调（prompt/readPassword）的模态输入语义。
/// kind 唯一数据源（TASK065 F2b — CommandDialogCoordinator.BeginPrompt 消费，
/// Password 形态渲染走 PasswordMask 遮蔽）。
/// </summary>
public enum InputRequestKind
{
    /// <summary>自由文本输入（prompt — 回车提交原文）。</summary>
    Text,

    /// <summary>密码输入（readPassword — 提交原文、渲染层走 PasswordMask 遮蔽）。</summary>
    Password,
}

/// <summary>挂起中的输入请求（提示文本 + 种类）。</summary>
public sealed record InputRequest(InputRequestKind Kind, string Prompt);

/// <summary>
/// 输入请求协调器（S8）— PermissionCoordinator 的 TCS 桥模式复刻（命令交互回调面）：
/// Begin 建立挂起请求（旧等待者取消），Complete 解除等待并清空（幂等）；
/// 消费方：Host 层命令回调桥（SlashCommandRunner prompt/readPassword 回调 → Begin → Wait 同步阻塞
/// → ReplShell 输入模态按键 Complete — S7 域接线，未接线期间命令侧回调保持 null 降级不挂起）。
/// </summary>
public sealed class InputRequestCoordinator
{
    private TaskCompletionSource<string?>? _pending;

    /// <summary>当前挂起请求（无挂起为 null）。</summary>
    public InputRequest? Request { get; private set; }

    /// <summary>是否挂起中（输入模态路由依据）。</summary>
    public bool IsPending => _pending is not null;

    /// <summary>等待用户输入（Host 回调同步阻塞 GetAwaiter().GetResult()；无挂起时为已完成 null 的占位任务）。</summary>
    public Task<string?> Wait() => (_pending ?? CompletedNull).Task;

    /// <summary>已完成 null 的占位任务（无挂起 Wait 的安全值）。</summary>
    private static readonly TaskCompletionSource<string?> CompletedNull = CreateCompleted(null);

    /// <summary>Begin 挂起请求 — 取消旧等待者（对齐 PermissionCoordinator 取代语义）。</summary>
    public void Begin(InputRequest request)
    {
        _pending?.TrySetCanceled();
        _pending = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Request = request;
    }

    /// <summary>Complete 解除等待 + 清空状态（null 输入 = 用户取消；幂等 — 二次调用 no-op）。</summary>
    public void Complete(string? input)
    {
        if (_pending is null)
        {
            return;
        }

        _pending.TrySetResult(input);
        _pending = null;
        Request = null;
    }

    /// <summary>构造已完成结果的占位 TCS。</summary>
    private static TaskCompletionSource<string?> CreateCompleted(string? input)
    {
        var source = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        source.TrySetResult(input);
        return source;
    }
}
