namespace NativeTui.Core.Bridge;

/// <summary>
/// bridge 请求（TUI → Engine，写子进程 stdin）— ADR 0141 请求契约：
/// submit（提交输入）/ interrupt（打断回合）/ set_permission_mode（切换权限模式）/
/// stop_task（停止后台任务 — M8-8 后续）/ quit（退出会话），
/// newline-delimited JSON，type 判别（[JsonPolymorphic] AOT 兼容多态）。
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(Submit), "submit")]
[JsonDerivedType(typeof(Interrupt), "interrupt")]
[JsonDerivedType(typeof(SetPermissionMode), "set_permission_mode")]
[JsonDerivedType(typeof(StopTask), "stop_task")]
[JsonDerivedType(typeof(Quit), "quit")]
public abstract record BridgeRequest
{
    /// <summary>提交用户输入，开始回合。</summary>
    /// <param name="Text">提交文本（图片占位 [Image #N] 已由 shell 剥离）。</param>
    /// <param name="Images">图片附件数组（TASK066 板块 G G4 bridge 协议图片通道 — null 不序列化字段
    ///（WhenWritingNull，对齐 DiffLine 行号字段扩展先例），旧对端 JSON 零破坏；与 jcc 侧
    /// BridgeRequest.Submit 逐字段镜像，JSON 形状由 BridgeJsonContractTests 锁定防漂移）。</param>
    public sealed record Submit(string Text,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] BridgeImageAttachment[]? Images = null) : BridgeRequest;

    /// <summary>打断当前回合。</summary>
    public sealed record Interrupt() : BridgeRequest;

    /// <summary>切换权限模式（mode 取值 plan/auto/ask/bypass/unattended，与引擎 [EnumValue] 一致）。</summary>
    public sealed record SetPermissionMode(string Mode) : BridgeRequest;

    /// <summary>停止后台任务（M8-8 后续 — kind: fork|teammate 分派引擎 CancelFork/StopTeammate；
    /// 与 jcc 侧 BridgeRequest.StopTask 逐字段镜像，JSON 形状由 BridgeJsonContractTests 锁定防漂移）。</summary>
    public sealed record StopTask(string Kind, string TaskId) : BridgeRequest;

    /// <summary>退出会话。</summary>
    public sealed record Quit() : BridgeRequest;
}
