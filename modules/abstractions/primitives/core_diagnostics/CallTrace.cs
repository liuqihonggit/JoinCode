namespace JoinCode.Abstractions.Diagnostics;

/// <summary>
/// 链路追踪 — 基于 AsyncLocal 自动跨异步边界传播调用 ID
/// 格式: {sessionId短码}.{递增序号}，如 88d5.0、88d5.1
/// 用法: LLMInvocationHandler 入口 SetId，下游各层通过 CurrentId 读取
/// </summary>
public static class CallTrace {
    private static readonly AsyncLocal<string?> _callId = new();

    /// <summary>当前调用链路 ID（无则 null）</summary>
    public static string? CurrentId => _callId.Value;

    /// <summary>设置当前调用链路 ID — 在 LLM 调用入口处设置</summary>
    public static void SetId(string id) => _callId.Value = id;

    /// <summary>清除当前调用链路 ID — 在 LLM 调用出口处清除</summary>
    public static void Clear() => _callId.Value = null;

    /// <summary>
    /// 进入调用链路作用域 — Dispose 时恢复原值(支持嵌套)
    /// </summary>
    public static IDisposable EnterScope(string id) => AsyncLocalScope<string>.Enter(_callId, id);
}