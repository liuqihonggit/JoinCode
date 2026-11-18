namespace JoinCode.Composition.Improvement;

/// <summary>
/// 经验库纯逻辑 — append-only 追加、三字段校验、按 cue 选择性检索（Propose）。
/// <para>零磁盘 IO：持久化由 CLI 层负责，本类只做纯函数，便于测试且不违反 JCC9001。</para>
/// </summary>
public static class ImprovementLibrary {
    /// <summary>
    /// 校验经验条目三字段完整（对齐 Harness² 的 has_experience：cue/lesson/evidence 缺一不可）。
    /// </summary>
    /// <param name="entry">待校验的经验条目。</param>
    /// <exception cref="ArgumentException">缺少 cue/lesson/evidence 中任一字段。</exception>
    public static void Validate(ImprovementEntry entry) {
        ArgumentNullException.ThrowIfNull(entry);
        if (string.IsNullOrWhiteSpace(entry.Cue))
            throw new ArgumentException(
                "经验条目缺少 cue（触发线索）—— 无 cue 则 Propose 无法匹配，经验等于写废。",
                nameof(entry));
        if (string.IsNullOrWhiteSpace(entry.Lesson))
            throw new ArgumentException(
                "经验条目缺少 lesson（可迁移教训）—— 只留证据不留教训，下个任务学不到东西。",
                nameof(entry));
        if (string.IsNullOrWhiteSpace(entry.Evidence))
            throw new ArgumentException(
                "经验条目缺少 evidence（行为证据）—— 无证据的教训是臆测，Harness² 靠行为对比而非空口断言。",
                nameof(entry));
    }

    /// <summary>
    /// append-only 追加：返回新集合，不改写历史条目（every run is traceable）。
    /// </summary>
    /// <param name="library">现有经验库。</param>
    /// <param name="entry">要追加的新条目（先经 <see cref="Validate"/> 校验）。</param>
    /// <returns>追加后的新集合。</returns>
    public static IReadOnlyList<ImprovementEntry> Append(IReadOnlyList<ImprovementEntry> library, ImprovementEntry entry) {
        Validate(entry);
        return [.. library, entry];
    }

    /// <summary>
    /// Propose — 按 cue 边界匹配返回候选经验（大小写不敏感），只读、不写入、不自动应用。
    /// <para>Harness² 红线：下个任务从 H₀ 重启，只通过 Propose 有选择地复用，绝不把命中项自动套到 harness 上。</para>
    /// <para>边界匹配：整词相等或以 '-' 分段前缀相等，避免 "web" 误命中 "webhook" 这类子串假阳性。</para>
    /// </summary>
    /// <param name="library">经验库。</param>
    /// <param name="cue">当前任务的触发线索。</param>
    /// <returns>cue 命中的候选条目；无命中返回空集合。</returns>
    public static IReadOnlyList<ImprovementEntry> Propose(IReadOnlyList<ImprovementEntry> library, string cue) {
        if (string.IsNullOrWhiteSpace(cue)) return [];
        return library.Where(entry => CueMatches(entry.Cue, cue)).ToArray();
    }

    /// <summary>cue 边界匹配：整词相等，或以 '-' 为分段边界的前缀关系。</summary>
    private static bool CueMatches(string entryCue, string cue) {
        if (string.IsNullOrWhiteSpace(entryCue)) return false; // 空 cue 不得通杀
        if (string.Equals(entryCue, cue, StringComparison.OrdinalIgnoreCase)) return true;
        if (entryCue.StartsWith(cue + "-", StringComparison.OrdinalIgnoreCase)) return true;
        if (cue.StartsWith(entryCue + "-", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>
    /// 解析单行 JSONL 经验条目并校验三字段 — 纯函数，供持久化层与测试共用。
    /// </summary>
    /// <param name="line">JSONL 的一行。</param>
    /// <param name="entry">解析成功且校验通过时的条目。</param>
    /// <param name="error">解析或校验失败时的四段式原因。</param>
    /// <returns>成功返回 true，否则 false。</returns>
    public static bool TryParseLine(string line, out ImprovementEntry? entry, out string? error) {
        entry = null;
        error = null;
        if (string.IsNullOrWhiteSpace(line)) {
            error = "经验条目行为空。";
            return false;
        }
        try {
            var parsed = JsonSerializer.Deserialize(line, ImprovementJsonContext.Default.ImprovementEntry);
            if (parsed is null) {
                error = "经验条目行反序列化结果为 null。";
                return false;
            }
            Validate(parsed);
            entry = parsed;
            return true;
        } catch (JsonException ex) {
            error = $"经验条目行不是合法 JSON：{ex.Message}";
            return false;
        } catch (ArgumentException ex) {
            error = $"经验条目三字段不完整：{ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// 从经验库 collect 指定 cue 命中的全部 patch（供启动时采纳）— 纯函数。
    /// </summary>
    /// <param name="library">经验库。</param>
    /// <param name="cue">采纳线索。</param>
    /// <returns>命中条目的 patch 集合（无 patch 的条目被忽略）。</returns>
    public static IReadOnlyList<HarnessPatch> CollectPatches(IReadOnlyList<ImprovementEntry> library, string cue) =>
        Propose(library, cue)
            .Where(entry => entry.Patch is not null)
            .Select(entry => entry.Patch ?? throw new InvalidOperationException("patch 在过滤后为空"))
            .ToArray();
}
