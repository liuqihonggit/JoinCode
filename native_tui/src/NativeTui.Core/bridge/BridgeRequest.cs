namespace NativeTui.Core.Bridge;

/// <summary>
/// bridge 请求（TUI → Engine，写子进程 stdin）— ADR 0141 请求契约：
/// submit（提交输入）/ interrupt（打断回合）/ quit（退出会话），
/// newline-delimited JSON，type 判别（[JsonPolymorphic] AOT 兼容多态）。
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(Submit), "submit")]
[JsonDerivedType(typeof(Interrupt), "interrupt")]
[JsonDerivedType(typeof(Quit), "quit")]
public abstract record BridgeRequest
{
    /// <summary>提交用户输入，开始回合。</summary>
    public sealed record Submit(string Text) : BridgeRequest;

    /// <summary>打断当前回合。</summary>
    public sealed record Interrupt() : BridgeRequest;

    /// <summary>退出会话。</summary>
    public sealed record Quit() : BridgeRequest;
}
