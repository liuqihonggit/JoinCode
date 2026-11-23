namespace JoinCode.App.Middlewares;

/// <summary>
/// UserPromptSubmit hook 执行器（TASK130-A5）— 单一实现，两个消费点共用：
/// ① 回落路径 <see cref="ChatTelemetryPreHook"/>（无主代理时走 chat 管道）；
/// ② 主代理路径 <see cref="JoinCode.Adapters.SessionController"/>（生产主链，此前完全不执行该 hook）。
/// <para>
/// 为什么必须共用：hook 的 <c>additionalContext</c> 与 <c>systemMessage</c> 过去只在 ① 被解析，
/// 而 ① 只在 <c>_mainAgent is null</c> 时跑 —— 生产主链上这两个字段从未生效（同 #53 的
/// 「声明≠消费」变体）。抽成一处，避免两条链各写一份又各自漂移。
/// </para>
/// </summary>
internal static class UserPromptSubmitHookRunner {
    /// <summary>执行结果 — Blocked 时调用方必须中止本回合（对齐基准 processUserInput 阻断语义）。</summary>
    internal sealed record Outcome(bool Blocked, string? AdditionalContext, IReadOnlyList<string> SystemNotices) {
        /// <summary>无 hook 提供者时的空结果（不阻断、无注入、无旁白）。</summary>
        internal static Outcome Empty { get; } = new(false, null, []);
    }

    /// <summary>执行 UserPromptSubmit hook；无编排器或异常一律 fail-soft 返回 <see cref="Outcome.Empty"/>。</summary>
    internal static async Task<Outcome> RunAsync(
        IHookOrchestrator? orchestrator, string prompt, string sessionId, CancellationToken ct) {
        if (orchestrator is null) {
            return Outcome.Empty;
        }

        var payload = new Dictionary<string, JsonElement> {
            ["prompt"] = JsonElementHelper.FromString(prompt),
            ["session_id"] = JsonElementHelper.FromString(sessionId),
        };

        var additionalContexts = new List<string>();
        var systemNotices = new List<string>();
        var blocked = false;

        await foreach (var result in orchestrator.ExecuteHooksAsync(
            HookEvent.UserPromptSubmit,
            payload,
            sessionId: sessionId,
            cancellationToken: ct).ConfigureAwait(false)) {
            if (result.Outcome == HookOutcome.Blocking || result.PreventContinuation) {
                blocked = true;
                break;
            }

            if (!string.IsNullOrEmpty(result.AdditionalContext)) {
                additionalContexts.Add(result.AdditionalContext);
            }

            // systemMessage 与 additionalContext 走不同出口：后者注入，前者只上屏
            //（对齐基准 hook_system_message attachment — 不进上下文历史）
            if (!string.IsNullOrEmpty(result.SystemMessage)) {
                systemNotices.Add(result.SystemMessage);
            }
        }

        return new Outcome(
            blocked,
            additionalContexts.Count > 0 ? string.Join(Environment.NewLine, additionalContexts) : null,
            systemNotices);
    }
}
