namespace JoinCode.CliCommands;

/// <summary>
/// probe 元命令 — TASK098 融合的行为对比门禁真机观测面。
/// <para>对 H₀ 与「显式 adopt 的经验 delta」各跑一次真实限流请求序列（复用生产 FixedWindowRateLimiter），
/// 比较 allow/deny 指纹决定采纳与否。对齐 Harness² Probe→Compose：靠行为差异而非配置打印或 ground truth 判定。</para>
/// <para>无 --adopt 时只输出 H₀ 指纹（基线观测）；有 --adopt 时输出 H₀/候选双侧指纹与门禁判定。</para>
/// </summary>
internal static class ProbeCommand {
    private const string AdoptOption = "--adopt";

    /// <summary>执行 probe 子命令。</summary>
    /// <param name="args">命令行参数（args[0] = "probe"）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>0 = 命令执行成功（含门禁 FAIL，fail-closed 回 H₀ 不算命令失败）；1 = 参数/装配错误。</returns>
    public static Task<int> ExecuteAsync(string[] args, CancellationToken ct) {
        var unknown = FlatSubCommandRouter.DetectUnknownOptions(
            args, CliArgCliOptionConstants.AllOptionNames,
            System.Collections.Frozen.FrozenSet.Create(StringComparer.Ordinal, AdoptOption));
        if (unknown is not null) {
            TerminalHelper.WriteError(unknown);
            return Task.FromResult(1);
        }

        var adoptCue = FlatSubCommandRouter.GetOptionValue(args, AdoptOption);
        var profiles = Composition.NodeRegistry.NodeProfileLoader.LoadFromResources();

        // 骨架 = YAML 继承链解析结果（TASK097 G5）；这一步不含任何经验，是干净的 H₀ 基底。
        var skeleton = new Composition.NodeRegistry.NodeProfileResolver(profiles).Resolve("base");
        var baseline = Composition.Improvement.BehaviorProbe.Fingerprint(skeleton);
        Console.Out.WriteLine($"[PROBE-H0] 真实请求 {baseline.Sent} 次 → allow={baseline.Allowed} deny={baseline.Denied}（max-requests={baseline.MaxRequests}，生产 FixedWindowRateLimiter）");

        if (string.IsNullOrWhiteSpace(adoptCue)) {
            Console.Out.WriteLine("[PROBE] 未指定 --adopt → 仅输出 H₀ 基线；经验未采纳，行为保持 H₀。");
            return Task.FromResult(0);
        }

        var libraryPath = Environment.GetEnvironmentVariable(Composition.Improvement.ImprovementStore.FileEnvVar)
            ?? System.IO.Path.Combine("improvement", "EXPERIENCE.jsonl");
        var library = Composition.Improvement.ImprovementStore.Load(IO.FileSystem.FileSystemFactory.Create(), libraryPath).Entries;
        var candidates = Composition.Improvement.ImprovementLibrary.Propose(library, adoptCue);

        // 经验 patch → Round delta（TASK098 F2：生命周期显式化），经统一 DeltaComposer 叠加。
        var roundDeltas = candidates
            .Where(e => e.Patch is not null)
            .Select(e => Composition.Improvement.CompositionDelta.FromExperience(
                e.Patch ?? throw new InvalidOperationException($"经验 '{e.Cue}' 的 patch 在过滤后仍为空"),
                e.Cue, e.Evidence))
            .ToArray();

        if (roundDeltas.Length == 0) {
            Console.Out.WriteLine($"[PROBE] cue='{adoptCue}' 未命中带 patch 的经验 → 无 Round delta，行为保持 H₀。");
            return Task.FromResult(0);
        }

        var handle = Composition.Improvement.DeltaComposer.ComposeSafe(skeleton, roundDeltas);
        try {
            var candidate = Composition.Improvement.BehaviorProbe.Fingerprint(handle.Current);
            var verdict = Composition.Improvement.BehaviorProbe.Compare(skeleton, handle.Current);
            Console.Out.WriteLine($"[PROBE-CANDIDATE] cue='{adoptCue}' 采纳 {roundDeltas.Length} 条 Round delta → allow={candidate.Allowed} deny={candidate.Denied}（max-requests={candidate.MaxRequests}）");
            var tag = verdict.Passed ? "PASS" : "FAIL";
            Console.Out.WriteLine($"[PROBE-VERDICT] {tag} | {verdict.Reason}");
            if (!verdict.Passed)
                Console.Out.WriteLine("[PROBE] 门禁拒绝 → fail-closed 回 H₀；经验未进入生产消费端（every run is traceable）。");
            return Task.FromResult(0);
        } finally {
            // Round 层回滚：句柄 Dispose 把 Current 恢复 H₀，兑现 G6 H₀ 红线；这里只取指纹不保留组合。
            handle.Dispose();
        }
    }
}
