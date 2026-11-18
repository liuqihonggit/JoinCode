namespace JoinCode.Composition.Improvement;

/// <summary>
/// 行为探针结果 — 一次真实请求序列的 allow/deny 指纹，不是配置值打印。
/// </summary>
/// <param name="Sent">发出的请求总数。</param>
/// <param name="Allowed">被限流器允许的请求数。</param>
/// <param name="Denied">被限流器拒绝的请求数。</param>
/// <param name="MaxRequests">本次探针读取到的窗口上限（来自 profile config）。</param>
public sealed record ProbeFingerprint(int Sent, int Allowed, int Denied, int MaxRequests);

/// <summary>
/// 行为对比判定 — 对齐 Harness² Probe 精神：靠行为差异而非 ground truth 打分。
/// </summary>
/// <param name="Passed">门禁是否通过。</param>
/// <param name="Reason">判定原因（真机可观测）。</param>
/// <param name="Baseline">H₀ 的行为指纹。</param>
/// <param name="Candidate">叠加 delta 后的行为指纹。</param>
public sealed record ProbeVerdict(bool Passed, string Reason, ProbeFingerprint Baseline, ProbeFingerprint Candidate);

/// <summary>
/// Probe 行为门禁 — 对 delta 采纳前后跑真实请求，比较行为差异，不依赖 ground truth。
/// <para>融合 Harness² 的 Probe→Compose 循环：Feedback 经验声称有效，必须在真实限流器上表现出
/// 可观测差异才放行；声称改变却零差异、或行为反向，一律 fail-closed 回 H₀。</para>
/// <para>复用生产同款 <see cref="FixedWindowRateLimiter"/>（Core.Utils），不是另写一套模拟逻辑。</para>
/// </summary>
public static class BehaviorProbe {
    /// <summary>探针默认请求数 — 大于 base(30) 与常见 patch(50)，确保能区分两档行为。</summary>
    public const int DefaultRequests = 60;

    private const string ConsumerId = "chat-pipeline";
    private const string NodeId = "stream-rate-limit-mw";
    private const string MaxRequestsKey = "max-requests";
    private const string WindowSecondsKey = "window-seconds";
    private const int DefaultWindowSeconds = 60;

    /// <summary>
    /// 对给定 profile 跑一次真实请求序列，返回行为指纹。
    /// </summary>
    /// <param name="profile">被测 profile（H₀ 或叠加 delta 后）。</param>
    /// <param name="requests">发出的请求数，须为正。</param>
    /// <returns>allow/deny 指纹。</returns>
    public static ProbeFingerprint Fingerprint(ResolvedProfile profile, int requests = DefaultRequests) {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(requests);

        var max = ReadPositiveInt(profile, MaxRequestsKey, 30);
        var windowSeconds = ReadPositiveInt(profile, WindowSecondsKey, DefaultWindowSeconds);
        var limiter = new FixedWindowRateLimiter(max, TimeSpan.FromSeconds(windowSeconds));

        var allowed = 0;
        for (var i = 0; i < requests; i++) {
            if (limiter.TryAcquire()) allowed++;
        }
        return new ProbeFingerprint(requests, allowed, requests - allowed, max);
    }

    /// <summary>
    /// 对比 H₀ 与候选 profile 的真实行为，判定 delta 是否有效。
    /// <para>判定规则（保守，避免挡住合法经验）：
    /// ① 候选行为与 H₀ 完全相同 → FAIL（经验声称改变却无行为差异，是死经验）；
    /// ② 候选允许数少于 H₀ → FAIL（行为反向，经验把系统改紧了，与目标相悖）；
    /// ③ 其余（允许数增加）→ PASS。</para>
    /// </summary>
    /// <param name="baseline">H₀ profile。</param>
    /// <param name="candidate">叠加 Round delta 后的 profile。</param>
    /// <param name="requests">请求数。</param>
    /// <returns>门禁判定，含两侧指纹与原因。</returns>
    public static ProbeVerdict Compare(ResolvedProfile baseline, ResolvedProfile candidate, int requests = DefaultRequests) {
        var b = Fingerprint(baseline, requests);
        var c = Fingerprint(candidate, requests);

        if (b.Allowed == c.Allowed)
            return new ProbeVerdict(false,
                $"行为零差异：H₀ 与候选均允许 {b.Allowed}/{requests} 次请求——经验声称改变但限流器行为未变（死经验），已拒绝采纳。",
                b, c);
        if (c.Allowed < b.Allowed)
            return new ProbeVerdict(false,
                $"行为反向：H₀ 允许 {b.Allowed} 次，候选仅允许 {c.Allowed} 次——经验把系统改紧了，与目标相悖，已拒绝采纳。",
                b, c);
        return new ProbeVerdict(true,
            $"行为差异成立：H₀ 允许 {b.Allowed}/{requests} → 候选允许 {c.Allowed}/{requests}（+{c.Allowed - b.Allowed}），经验真实改变限流行为，采纳。",
            b, c);
    }

    private static int ReadPositiveInt(ResolvedProfile profile, string key, int fallback) {
        if (!profile.Consumers.TryGetValue(ConsumerId, out var consumer)) return fallback;
        var node = consumer.Nodes.FirstOrDefault(n => n.Id == NodeId);
        if (node is null || !node.Config.TryGetValue(key, out var raw)) return fallback;
        return int.TryParse(raw, out var value) && value > 0 ? value : fallback;
    }
}
