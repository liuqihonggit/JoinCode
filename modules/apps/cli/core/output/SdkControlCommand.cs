namespace JoinCode.Cli.Output;

/// <summary>
/// SDK 控制请求的**执行视图**（TASK144 骨架）— 把一条 <c>control_request</c> 从「JSON 行」降到
/// 「request_id + subtype + 原始 request 对象」，让 subtype 的执行方不必认识整行外壳。
/// <para>为什么 payload 保留 <c>JsonElement</c> 而不逐 subtype 加 DTO 字段：每接一个新 subtype
/// （<c>set_model</c> 的 <c>model</c>、<c>initialize</c> 的能力清单）都要回到共享 DTO 上加字段，
/// 字段名还会被 AOT 序列化上下文牵住。字段归各 handler 自读，新增 subtype 不动共享类型。</para>
/// </summary>
public sealed record SdkControlCommand {
    /// <summary>
    /// 控制请求 ID — 应答必须原样带回。空值属于协议错误（无从回包），由调用方在构造前丢弃。
    /// </summary>
    public required string RequestId { get; init; }

    /// <summary>请求子类型（<c>interrupt</c> / <c>set_permission_mode</c> / …），即 handler 的注册键。</summary>
    public required string Subtype { get; init; }

    /// <summary>
    /// <c>request</c> 对象原名（含 subtype 与各子类型自己的字段）；缺省为 <c>Undefined</c>，
    /// 读取方用 <see cref="SdkControlPayload.ReadString"/> 之类的宽松读取。
    /// </summary>
    public System.Text.Json.JsonElement Payload { get; init; }
}

/// <summary>
/// 一条控制请求的处理结果（TASK144 骨架）— <see cref="Response"/> 为 <c>required</c>，
/// 把「只要对方给了 request_id 就必须回包」这条协议不变量交给类型系统，而不是靠调用方记得。
/// </summary>
public sealed record SdkControlOutcome {
    /// <summary>
    /// 必须写回 stdout 的应答（成功或 error）。没有「不回包」的合法形态 ——
    /// 基准侧 print.ts 对未知 subtype 也回 error，否则调用方一直等一条不会来的应答。
    /// </summary>
    public required SdkStreamControlResponse Response { get; init; }

    /// <summary>true = 处理该请求需要取消在途回合（当前仅 <c>interrupt</c>）。</summary>
    public bool CancelActiveTurn { get; init; }

    /// <summary>
    /// 需要写 stderr 的附加诊断（stdout 是纯协议流）；null = 无。拒绝时即拒绝原因，
    /// 调用方自行决定前缀（现网是 <c>[INPUT] </c>）。
    /// </summary>
    public string? Diagnostic { get; init; }

    /// <summary>确认收到 — 空载荷（对齐基准 <c>response:{}</c>），用于 interrupt 这类无返回值的指令。</summary>
    /// <param name="requestId">原请求 ID</param>
    /// <returns>处理结果</returns>
    public static SdkControlOutcome Acknowledge(string requestId) => new() {
        Response = SdkStreamControlResponse.Success(requestId),
    };

    /// <summary>
    /// 确认收到 — 带回**实际生效**值（如权限模式：bypass 可能被安全闸拦成别的值，
    /// 不回实际值客户端就无从知道真实状态）。
    /// </summary>
    /// <param name="requestId">原请求 ID</param>
    /// <param name="effectiveMode">实际生效的模式</param>
    /// <returns>处理结果</returns>
    public static SdkControlOutcome Acknowledge(string requestId, string effectiveMode) => new() {
        Response = SdkStreamControlResponse.Success(requestId, effectiveMode),
    };

    /// <summary>取消在途回合 — 应答空载荷，另带取消意图。</summary>
    /// <param name="requestId">原请求 ID</param>
    /// <returns>处理结果</returns>
    public static SdkControlOutcome Interrupt(string requestId) => new() {
        Response = SdkStreamControlResponse.Success(requestId),
        CancelActiveTurn = true,
    };

    /// <summary>
    /// 拒绝 — 回 error 分支，原因同时进 <see cref="Diagnostic"/>。
    /// <para>拒绝**不是**静默跳过：只写 stderr 不回包会让客户端永久等待。</para>
    /// </summary>
    /// <param name="requestId">原请求 ID</param>
    /// <param name="reason">可读原因（应带「下一步怎么做」）</param>
    /// <returns>处理结果</returns>
    public static SdkControlOutcome Reject(string requestId, string reason) => new() {
        Response = SdkStreamControlResponse.Failure(requestId, reason),
        Diagnostic = reason,
    };
}

/// <summary>
/// 控制请求 payload 的宽松读取（TASK144 骨架）— payload 是客户端给的 JSON，字段缺失、
/// 类型不符都只当作「没给」，由 handler 决定回什么 error，不抛异常。
/// </summary>
public static class SdkControlPayload {
    /// <summary>读一个字符串字段；缺失、null 或非字符串都返回 null。</summary>
    /// <param name="payload"><c>request</c> 对象原名</param>
    /// <param name="name">字段名</param>
    /// <returns>字段值或 null</returns>
    public static string? ReadString(System.Text.Json.JsonElement payload, string name) {
        if (payload.ValueKind != System.Text.Json.JsonValueKind.Object)
            return null;
        return payload.TryGetProperty(name, out var element)
               && element.ValueKind == System.Text.Json.JsonValueKind.String
            ? element.GetString()
            : null;
    }
}
