namespace JoinCode.Composition.Improvement;

/// <summary>
/// 经验库持久化 + 启动期采纳桥接 — 把「显式 adopt 的经验」接进真实启动链。
/// <para>数据来源：JSONL 文件（append-only，由 jcc experience reflect 写入）+ 环境变量 JCC_EXPERIENCE_ADOPT（本轮采纳的 cue）。
/// 被拒条目写到 &lt;file&gt;.rejected 隔离文件，保持 every run is traceable，绝不静默丢弃。</para>
/// </summary>
public static class ImprovementStore {
    /// <summary>采纳 cue 环境变量名 — 设置后，本次启动从 H₀ + 匹配经验 patch 构造 profile。</summary>
    public const string AdoptEnvVar = "JCC_EXPERIENCE_ADOPT";

    /// <summary>经验库文件环境变量名。</summary>
    public const string FileEnvVar = "JCC_IMPROVEMENT_FILE";

    /// <summary>
    /// 读取本轮应采纳的 patch — 未设置 <see cref="AdoptEnvVar"/> 或库为空时返回空集合（即纯 H₀）。
    /// </summary>
    /// <param name="fs">文件系统抽象，用于读取经验库 JSONL。</param>
    /// <returns>本轮显式采纳的 patch；无则空。</returns>
    public static IReadOnlyList<HarnessPatch> LoadAdoptedPatches(IFileSystem fs) {
        var adoptCue = Environment.GetEnvironmentVariable(AdoptEnvVar);
        if (string.IsNullOrWhiteSpace(adoptCue)) return [];

        var path = Environment.GetEnvironmentVariable(FileEnvVar)
            ?? System.IO.Path.Combine("improvement", "EXPERIENCE.jsonl");
        var (entries, rejected) = Load(fs, path);
        if (rejected.Count > 0) {
            Console.Error.WriteLine(
                $"[EXPERIENCE] 经验库有 {rejected.Count} 条被拒条目（采纳 cue='{adoptCue}'）；从残缺库采纳存在风险，已拒绝采纳。" +
                " 触发参数: " + path + ".rejected。拦截层: ImprovementStore 启动采纳层。" +
                " 正确写法: 修正 .rejected 中的条目后重新 reflect，或清空库从 H₀ 启动。");
            return []; // fail-closed：库不干净就不采纳
        }
        return ImprovementLibrary.CollectPatches(entries, adoptCue);
    }

    /// <summary>
    /// 从 JSONL 加载经验库 — 逐行校验，合法条目入内存库，被拒条目返回以便隔离。
    /// </summary>
    /// <param name="fs">文件系统抽象（测试可注入 InMemory 实现，0 磁盘 IO）。</param>
    /// <param name="path">JSONL 文件路径。</param>
    /// <returns>(合法条目集合, 被拒原始行集合)。</returns>
    public static ImprovementLoadResult Load(IFileSystem fs, string path) {
        if (!fs.FileExists(path)) return new ImprovementLoadResult([], []);
        var entries = new List<ImprovementEntry>();
        var rejected = new List<string>();
        foreach (var line in fs.ReadAllLines(path)) {
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (ImprovementLibrary.TryParseLine(line, out var entry, out var error)) {
                if (entry is not null) entries.Add(entry);
            } else {
                rejected.Add(line);
                Console.Error.WriteLine($"[EXPERIENCE] 跳过被拒经验条目：{error} | 原始行: {line[..Math.Min(80, line.Length)]}");
            }
        }
        return new ImprovementLoadResult(entries, rejected);
    }
}

/// <summary>
/// 经验库加载结果 — 合法条目集合与被拒原始行集合（被拒条目单独返回以便隔离）。
/// </summary>
/// <param name="Entries">解析成功的经验条目。</param>
/// <param name="Rejected">校验失败、被隔离的原始行。</param>
public sealed record ImprovementLoadResult(
    IReadOnlyList<JoinCode.Composition.Improvement.ImprovementEntry> Entries,
    IReadOnlyList<string> Rejected);
