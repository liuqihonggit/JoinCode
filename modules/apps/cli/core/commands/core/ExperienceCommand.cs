namespace JoinCode.CliCommands;

/// <summary>
/// experience 元命令 — Harness² 式经验继承的真机观测面（G6，TASK097）。
/// <para>三个动作：
/// <list type="bullet">
/// <item><c>reflect</c>：追加一条 cue/lesson/evidence 经验到 append-only JSONL 库。</item>
/// <item><c>propose</c>：按 cue 列出候选经验（只读，不自动应用）。</item>
/// <item><c>round</c>：从 H₀（base profile）全新解析本轮 harness；--adopt 才叠加 patch，否则严格等于 H₀（预览）。</item>
/// <item><c>effective</c>：走完整宿主读 IProfileManager.Current —— 证明经验 patch 真实进入生产消费端（非预览）。</item>
/// </list>
/// </para>
/// <para>红线（论文实证 Code 73.4→63.2）：每轮 round 都重新 Resolve("base")，绝不接上一轮成品；
/// 经验只经显式 --adopt 生效，Propose 命中不等于采纳。</para>
/// </summary>
internal static class ExperienceCommand {
    private const string CueOption = "--cue";
    private const string LessonOption = "--lesson";
    private const string EvidenceOption = "--evidence";
    private const string PatchOption = "--patch";
    private const string AdoptOption = "--adopt";

    /// <summary>经验库默认路径 — 相对当前工作目录，可用 JCC_IMPROVEMENT_FILE 覆盖。</summary>
    private static string LibraryPath =>
        Environment.GetEnvironmentVariable("JCC_IMPROVEMENT_FILE")
        ?? System.IO.Path.Combine("improvement", "EXPERIENCE.jsonl");

    /// <summary>执行 experience 子命令。</summary>
    public static async Task<int> ExecuteAsync(string[] args, CancellationToken ct) {
        var allowed = System.Collections.Frozen.FrozenSet.Create(
            StringComparer.Ordinal, CueOption, LessonOption, EvidenceOption, PatchOption, AdoptOption);
        var unknown = FlatSubCommandRouter.DetectUnknownOptions(args, CliArgCliOptionConstants.AllOptionNames, allowed);
        if (unknown is not null) {
            TerminalHelper.WriteError(unknown);
            return 1;
        }

        var action = FlatSubCommandRouter.GetPositional(args, 0);
        return action switch {
            "reflect" => Reflect(args),
            "propose" => Propose(args),
            "round" => Round(args),
            "effective" => await EffectiveAsync(args, ct).ConfigureAwait(false),
            null or "" => Usage("缺少动作（experience reflect|propose|round|effective）"),
            _ => Usage($"未知动作 '{action}'"),
        };
    }

    /// <summary>
    /// effective — 走完整宿主读取 IProfileManager.Current（限流中间件的真实消费对象），
    /// 证明 JCC_EXPERIENCE_ADOPT 的经验 patch 真的进入了生产启动链，而非只是 round 预览。
    /// </summary>
    private static Task<int> EffectiveAsync(string[] args, CancellationToken ct) =>
        McpCliCommand.WithHostAsync(services => {
            var profileManager = services.GetService<Abstractions.Composition.IProfileManager>();
            if (profileManager is null) {
                TerminalHelper.WriteError("IProfileManager 未注册（experience effective 宿主装配层拦截）：宿主没有加载 profile 服务。");
                return Task.FromResult(1);
            }
            var current = profileManager.Current;
            var maxRequests = ConfigOf(current, "chat-pipeline", "stream-rate-limit-mw", "max-requests");
            var adoptCue = Environment.GetEnvironmentVariable(Composition.Improvement.ImprovementStore.AdoptEnvVar);
            Console.Out.WriteLine($"[EXPERIENCE-EFFECTIVE] JCC_EXPERIENCE_ADOPT='{adoptCue ?? "(未设置)"}' → IProfileManager.Current.max-requests={maxRequests}（生产消费端真实值，非 round 预览）");
            return Task.FromResult(0);
        }, ct: ct);

    /// <summary>reflect — 追加一条经验；--patch 格式 consumer/node/configKey=value。</summary>
    private static int Reflect(string[] args) {
        var cue = FlatSubCommandRouter.GetOptionValue(args, CueOption);
        var lesson = FlatSubCommandRouter.GetOptionValue(args, LessonOption);
        var evidence = FlatSubCommandRouter.GetOptionValue(args, EvidenceOption);
        var patchRaw = FlatSubCommandRouter.GetOptionValue(args, PatchOption);

        Composition.Improvement.HarnessPatch? patch = null;
        if (!string.IsNullOrWhiteSpace(patchRaw)) {
            if (!TryParsePatch(patchRaw, out patch, out var parseError)) {
                TerminalHelper.WriteError(parseError ?? "patch 格式无法解析");
                return 1;
            }
        }

        var entry = new Composition.Improvement.ImprovementEntry(cue ?? "", lesson ?? "", evidence ?? "", patch);
        try {
            Composition.Improvement.ImprovementLibrary.Validate(entry);
        } catch (ArgumentException ex) {
            TerminalHelper.WriteError($"经验条目无效（experience reflect 校验层拦截）：{ex.Message}");
            TerminalHelper.WriteError("正确写法: jcc experience reflect --cue rate-limit --lesson '调大阈值' --evidence '压测无 429' [--patch chat-pipeline/stream-rate-limit-mw/max-requests=50]");
            return 1;
        }

        var path = LibraryPath;
        var dir = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        // ConfigKey 存在性校验：patch 必须改 H₀ 中真实存在的键，否则是死配置（评审 P1）。
        if (patch is not null) {
            var h0 = Composition.Improvement.HarnessComposer.Compose(
                Composition.NodeRegistry.NodeProfileLoader.LoadFromResources(fileSystem: new IO.FileSystem.PhysicalFileSystem()), "base", []);
            if (!h0.Consumers.TryGetValue(patch.Consumer, out var cc)
                || cc.Nodes.FirstOrDefault(n => n.Id == patch.Node) is not { } target) {
                TerminalHelper.WriteError($"patch 目标 consumer/node 在 H₀ 中不存在（experience reflect patch 校验层拦截）：{patch.Consumer}/{patch.Node}。");
                TerminalHelper.WriteError("正确写法: 指向 base.yaml 中已有的节点，例 chat-pipeline/stream-rate-limit-mw/max-requests=50");
                return 1;
            }
            if (!target.Config.ContainsKey(patch.ConfigKey)) {
                TerminalHelper.WriteError($"patch 的 config 键 '{patch.ConfigKey}' 在 H₀ 节点 '{patch.Node}' 中不存在（experience reflect patch 校验层拦截）。");
                TerminalHelper.WriteError($"可用键: {string.Join(", ", target.Config.Keys.Order())}。正确写法: 选一个 H₀ 已有的键；经验只覆写、不凭空新增配置。");
                return 1;
            }
        }

        var library = LoadLibrary(path);
        var appended = Composition.Improvement.ImprovementLibrary.Append(library, entry);
        File.AppendAllText(path, Serialize(entry) + Environment.NewLine);

        Console.Out.WriteLine($"[EXPERIENCE-REFLECT] 追加经验 cue='{entry.Cue}' → library 共 {appended.Count} 条（append-only，历史不变）");
        return 0;
    }

    /// <summary>propose — 按 cue 列出候选（只读）。</summary>
    private static int Propose(string[] args) {
        var cue = FlatSubCommandRouter.GetOptionValue(args, CueOption);
        if (string.IsNullOrWhiteSpace(cue)) {
            TerminalHelper.WriteError("propose 缺少 --cue（experience propose 参数层拦截）。");
            TerminalHelper.WriteError("正确写法: jcc experience propose --cue rate-limit");
            return 1;
        }

        var library = LoadLibrary(LibraryPath);
        var candidates = Composition.Improvement.ImprovementLibrary.Propose(library, cue);
        Console.Out.WriteLine($"[EXPERIENCE-PROPOSE] cue='{cue}' → 命中 {candidates.Count} 条候选（只读，未自动应用）");
        for (var i = 0; i < candidates.Count; i++) {
            var e = candidates[i];
            var patchTag = e.Patch is null ? "无 patch（仅教训）" : $"patch={e.Patch.Consumer}/{e.Patch.Node}/{e.Patch.ConfigKey}={e.Patch.ConfigValue}";
            Console.Out.WriteLine($"  [{i}] cue={e.Cue} | {e.Lesson} | {patchTag}");
        }
        return 0;
    }

    /// <summary>round — 从 H₀ 重建本轮 harness，--adopt 才叠加 patch。</summary>
    private static int Round(string[] args) {
        var adoptCue = FlatSubCommandRouter.GetOptionValue(args, AdoptOption);
        var profiles = Composition.NodeRegistry.NodeProfileLoader.LoadFromResources(fileSystem: new IO.FileSystem.PhysicalFileSystem());

        // 红线：无条件重新解析 base，绝不接收上一轮 ResolvedProfile。
        var h0 = Composition.Improvement.HarnessComposer.Compose(profiles, "base", []);
        var h0Requests = ConfigOf(h0, "chat-pipeline", "stream-rate-limit-mw", "max-requests");

        Console.Out.WriteLine($"[G6-ROUND] 初始状态 == H₀: chat-pipeline/stream-rate-limit-mw.max-requests={h0Requests}（base 基底，未采纳任何经验）");

        var adopted = 0;
        var applied = h0;
        if (!string.IsNullOrWhiteSpace(adoptCue)) {
            var library = LoadLibrary(LibraryPath);
            var candidates = Composition.Improvement.ImprovementLibrary.Propose(library, adoptCue);
            var patches = candidates
                .Where(e => e.Patch is not null)
                .Select(e => e.Patch ?? throw new InvalidOperationException($"经验 '{e.Cue}' 的 patch 在过滤后仍为空"))
                .ToArray();
            try {
                applied = Composition.Improvement.HarnessComposer.Compose(profiles, "base", patches);
                adopted = patches.Length;
            } catch (InvalidOperationException ex) {
                TerminalHelper.WriteError($"采纳经验失败（experience round 组装层拦截）：{ex.Message}");
                return 1;
            }
        }

        var afterRequests = ConfigOf(applied, "chat-pipeline", "stream-rate-limit-mw", "max-requests");
        var adoptTag = adopted == 0 ? "未 adopt → 严格等于 H₀" : "显式 adopt → 才改变";
        Console.Out.WriteLine($"[G6-ROUND] 采纳 {adopted} 条 patch 后: max-requests={afterRequests}（{adoptTag}）");
        return 0;
    }

    private static string? ConfigOf(Abstractions.Composition.Profile.ResolvedProfile profile, string consumer, string node, string key) =>
        profile.Consumers.TryGetValue(consumer, out var c)
            ? c.Nodes.FirstOrDefault(n => n.Id == node)?.Config.GetValueOrDefault(key)
            : null;

    private static bool TryParsePatch(string raw, out Composition.Improvement.HarnessPatch? patch, out string? error) {
        patch = null;
        error = null;
        // 格式: consumer/node/configKey=value
        var slash1 = raw.IndexOf('/');
        var slash2 = slash1 < 0 ? -1 : raw.IndexOf('/', slash1 + 1);
        var eq = raw.IndexOf('=');
        var consumer = slash1 > 0 ? raw[..slash1] : "";
        var node = slash2 > slash1 + 1 ? raw[(slash1 + 1)..slash2] : "";
        var key = eq > slash2 + 1 ? raw[(slash2 + 1)..eq] : "";
        var value = eq >= 0 && eq < raw.Length - 1 ? raw[(eq + 1)..] : "";
        if (string.IsNullOrWhiteSpace(consumer) || string.IsNullOrWhiteSpace(node)
            || string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value)) {
            error = $"--patch 格式或取值不完整（experience reflect 参数层拦截）：收到 '{raw}'。" +
                "触发参数: consumer/node/key/value 均须非空。" +
                "正确写法: consumer/node/configKey=value，例 chat-pipeline/stream-rate-limit-mw/max-requests=50";
            return false;
        }
        patch = new Composition.Improvement.HarnessPatch(consumer, node, key, value);
        return true;
    }

    private static IReadOnlyList<Composition.Improvement.ImprovementEntry> LoadLibrary(string path) =>
        Composition.Improvement.ImprovementStore.Load(IO.FileSystem.FileSystemFactory.Create(), path).Entries;

    private static string Serialize(Composition.Improvement.ImprovementEntry entry) =>
        JsonSerializer.Serialize(entry, Composition.Improvement.ImprovementJsonContext.Default.ImprovementEntry);

    private static int Usage(string error) {
        TerminalHelper.WriteError($"{error}（experience 参数层拦截）。");
        TerminalHelper.WriteError("用法: jcc experience reflect --cue C --lesson L --evidence E [--patch consumer/node/key=value] | propose --cue C | round [--adopt C] | effective（需 JCC_EXPERIENCE_ADOPT）");
        return 1;
    }
}
