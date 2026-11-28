namespace JoinCode.Cli.Output;

/// <summary>
/// 单个 <c>control_request</c> 子类型的处理契约（TASK144 骨架）— 「执行 + 应答」收在一处，
/// 由 <see cref="SdkControlPlane"/> 按 <see cref="Subtype"/> 注册分发。
/// <para>接入一个新 subtype 的最小代价 = 实现本接口 + 构造时注册一行：不必改共享 DTO、
/// 不必在宿主读循环里加分支，也不必自己记得「必须回包」（由
/// <see cref="SdkControlOutcome.Response"/> 强制）。</para>
/// </summary>
public interface ISdkControlHandler {
    /// <summary>
    /// 负责的子类型（协议字符串，大小写不敏感匹配）。即注册键，同一 plane 内不得重复。
    /// </summary>
    string Subtype { get; }

    /// <summary>
    /// 处理一条控制请求。实现方**不得**抛异常来表达拒绝 —— 应该返回
    /// <see cref="SdkControlOutcome.Reject"/>，这样客户端拿到的是可读 error 而不是一条超时。
    /// <para>plane 仍会兜底捕获异常转 error（防御性），但那是最后一道网、不应作为正常路径。</para>
    /// </summary>
    /// <param name="command">控制请求的执行视图（request_id + subtype + 原始 request 对象）</param>
    /// <returns>处理结果（必须带应答）</returns>
    SdkControlOutcome Handle(SdkControlCommand command);
}
