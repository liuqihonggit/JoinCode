namespace MockServer.E2E.Tests.Scripts;

/// <summary>
/// WQ 组限流 E2E 脚本 — C2「低限额进程级验证」的对话脚本（TASK131 P1-1）。
/// <para>
/// 场景：外部 profile（<c>JCC_PROFILE_DIR</c> 指向临时目录，含 <c>c2.yaml</c> =
/// <c>inherits: base</c> + 把 <c>query-pipeline</c> 的 <c>query-rate-limit-mw.max-requests</c>
/// 覆写为 1）启动 <c>bridge_serve</c>，同一进程内提交两个回合：
/// 第 1 回合消耗唯一限额并真实进入 MockServer；第 2 回合必须在限流中间件被拦
/// （<c>RateLimitExceededException</c> → bridge error 事件），请求不进入 MockServer。
/// </para>
/// <para>
/// 为什么用 bridge_serve 双回合而不是两次 <c>jcc -p</c>：限流计数器是进程内状态
/// （QueryEngine 的 MiddlewarePipeline 单例），每次 <c>jcc -p</c> 都是新进程新计数器，
/// 两次独立进程永远打不满限额 —— 只有单进程多回合才能复现「第 2 次查询被拒」。
/// </para>
/// </summary>
public static class WqQueryRateLimitScripts {
    /// <summary>
    /// wq01: 低限额（max-requests=1）下第二次查询被拦 —
    /// 第 1 回合正常回复；第 2 回合报「速率限制」且 MockServer 不新增请求。
    /// </summary>
    /// <param name="profileDir">含 <c>c2.yaml</c> 的外部 profile 目录（测试类负责创建与清理）。</param>
    public static ConversationScript Wq01LowLimitSecondTurnBlocked(string profileDir) => new() {
        Name = "wq01 C2 低限额第二次查询被拦且不进 MockServer",
        AdditionalArgs = "--profile c2",
        ExtraEnvVars = new Dictionary<string, string> {
            ["JCC_PROFILE_DIR"] = profileDir,
            // 与 ci-e2e.yml 顶层环境对齐：E2E 无 Hub 环境，bridge_serve 走直连回退
            ["JCC_ALLOW_DIRECT_FALLBACK"] = "1",
        },
        Turns =
        [
            new ConversationTurn {
                UserInput = "第一轮：请正常回复",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "第一轮正常回复。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "wq01: 第 1 回合消耗唯一限额，应正常回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "wq01: 第 1 回合不应有错误" },
                ]
            },
            new ConversationTurn {
                UserInput = "第二轮：这次应该被限流拦截",
                AiResponse = new MockResponseScript {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "这行文本永远不应出现：限额已耗尽，查询不得进入模型。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "速率限制", Description = "wq01: 第 2 回合应报 RateLimitExceededException（限流文案）" },
                    new OutputAssert { Type = AssertType.NotContainsText, Expected = "永远不应出现", Description = "wq01: 第 2 回合不应得到模型回复（请求未进入 MockServer）" },
                ]
            }
        ]
    };
}
