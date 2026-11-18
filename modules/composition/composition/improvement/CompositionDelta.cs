namespace JoinCode.Composition.Improvement;

/// <summary>
/// Delta 生命周期 — 融合 DSH「长期结构覆盖」与 Harness²「单轮经验覆盖」的唯一维度。
/// <para>Permanent 随 profile 存在（结构层，省略即继承）；Round 只活一轮，句柄 Dispose 或进程结束即弃（经验层，H₀ 红线）。</para>
/// </summary>
public enum DeltaLifetime {
    /// <summary>长期 delta：来自 profile inherits/overrides/includes，随 profile 存活。</summary>
    Permanent,

    /// <summary>单轮 delta：来自显式 adopt 的经验 patch，本轮结束即回滚到 H₀。</summary>
    Round,
}

/// <summary>
/// 统一差异单元 — DSH 结构覆盖与 Harness² 经验 patch 的融合表示。
/// <para>两者本质都是「对骨架的 consumer/node/configKey 键级覆写 + 后写者胜」，区别只在 <see cref="Lifetime"/>。
/// 这一个类型让合并只有一套语义，消除「两套继承并存」。</para>
/// </summary>
/// <param name="Consumer">目标消费点 ID。</param>
/// <param name="Node">目标节点 ID。</param>
/// <param name="ConfigKey">要覆写的 config 键。</param>
/// <param name="ConfigValue">覆写后的值。</param>
/// <param name="Lifetime">生命周期：Permanent（结构）/ Round（经验）。</param>
/// <param name="Cue">触发线索，仅 Round 有意义（对齐 Harness² cue/lesson/evidence）。</param>
/// <param name="Evidence">行为证据，仅 Round 有意义，Probe 门禁据此判定。</param>
public sealed record CompositionDelta(
    string Consumer,
    string Node,
    string ConfigKey,
    string ConfigValue,
    DeltaLifetime Lifetime,
    string? Cue = null,
    string? Evidence = null) {

    /// <summary>把结构 override 转成 Permanent delta。</summary>
    /// <param name="consumer">目标消费点。</param>
    /// <param name="node">目标节点。</param>
    /// <param name="key">config 键。</param>
    /// <param name="value">覆盖值。</param>
    /// <returns>永久 delta。</returns>
    public static CompositionDelta Permanent(string consumer, string node, string key, string value) =>
        new(consumer, node, key, value, DeltaLifetime.Permanent);

    /// <summary>把经验 patch 转成 Round delta，携带 cue 与 evidence 供 Probe 判定。</summary>
    /// <param name="patch">经验里的可复用 patch。</param>
    /// <param name="cue">触发线索。</param>
    /// <param name="evidence">行为证据。</param>
    /// <returns>单轮 delta。</returns>
    public static CompositionDelta FromExperience(HarnessPatch patch, string? cue, string? evidence) =>
        new(patch.Consumer, patch.Node, patch.ConfigKey, patch.ConfigValue, DeltaLifetime.Round, cue, evidence);
}

/// <summary>
/// 组合句柄 — 融合 G1 可逆注册与 G6 H₀ 重置的统一回滚单元。
/// <para>持有 H₀（只含 Permanent delta 的干净态）与本轮 Round delta 叠加后的 Current；
/// Dispose 一次性做两件事：LIFO 回滚本轮节点注册（G1），再把 Current 恢复到 H₀（G6）。
/// 这样「可逆装卸」与「H₀ 重置」不再是两条无关的线。</para>
/// </summary>
public sealed class CompositionHandle : IDisposable {
    private readonly List<IDisposable> _registrations = [];
    private int _disposed;

    /// <summary>干净基底 H₀ — 只含 Permanent delta，永不混入 Round。</summary>
    public ResolvedProfile H0 { get; }

    /// <summary>当前组装结果 — H₀ 叠加本轮 Round delta。</summary>
    public ResolvedProfile Current { get; private set; }

    /// <summary>本轮采纳的 Round delta 数量。</summary>
    public int AdoptedRoundCount { get; }

    /// <summary>是否已回滚。</summary>
    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    /// <summary>构造组合句柄。</summary>
    /// <param name="h0">干净基底。</param>
    /// <param name="current">叠加 Round 后的当前结果。</param>
    /// <param name="adoptedRoundCount">采纳的 Round delta 数。</param>
    public CompositionHandle(ResolvedProfile h0, ResolvedProfile current, int adoptedRoundCount) {
        H0 = h0;
        Current = current;
        AdoptedRoundCount = adoptedRoundCount;
    }

    /// <summary>登记一个需要随本轮回滚的注册句柄（G1 挂接点）。</summary>
    /// <param name="handle">节点注册句柄；已 Dispose 时立即回滚它并抛异常。</param>
    public void Track(IDisposable handle) {
        ArgumentNullException.ThrowIfNull(handle);
        if (IsDisposed) {
            handle.Dispose();
            throw new ObjectDisposedException(nameof(CompositionHandle), "组合句柄已回滚，不能再登记注册（登记句柄已即时回滚）。");
        }
        _registrations.Add(handle);
    }

    /// <summary>回滚本轮：先 LIFO 撤销节点注册，再把 Current 恢复到 H₀。幂等。</summary>
    public void Dispose() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        // Reverse() 保证 G1 的 LIFO 后注册先回滚；foreach(_registrations) 形式满足 JCC9306 完备释放检测。
        foreach (var registration in _registrations.AsEnumerable().Reverse())
            registration.Dispose();
        _registrations.Clear();
        Current = H0;
    }
}
