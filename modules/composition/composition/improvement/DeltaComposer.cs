namespace JoinCode.Composition.Improvement;

/// <summary>
/// 统一 Delta 合并器 — DSH 结构覆盖与 Harness² 经验 patch 的唯一键级覆写入口。
/// <para>设计要点：不重写已验收的 <see cref="NodeProfileResolver.Merge"/>（它处理 YAML 继承链的
/// excludes/includes/priority 专门语义），而是在它产出的骨架 <see cref="ResolvedProfile"/> 之上，
/// 用同一个键级覆写原语叠加所有 Delta。Permanent 先应用构成 H₀，Round 后应用构成 Current。</para>
/// </summary>
public static class DeltaComposer {
    /// <summary>
    /// 从骨架与 delta 集合组装本轮组合，返回可回滚句柄。
    /// <para>H₀ = 骨架 + 全部 Permanent delta（Round 被过滤）；Current = H₀ + 全部 Round delta。
    /// 无 delta 时 Current 严格等于骨架——这是 H₀ 红线在类型层的表达。</para>
    /// </summary>
    /// <param name="skeleton">YAML 继承链解析后的骨架 profile。</param>
    /// <param name="deltas">本轮全部 delta（Permanent 结构层 + Round 经验层）。</param>
    /// <returns>可回滚句柄；Dispose 后 Current 回到 H₀。</returns>
    public static CompositionHandle Compose(
        ResolvedProfile skeleton,
        IReadOnlyList<CompositionDelta> deltas) {
        ArgumentNullException.ThrowIfNull(skeleton);
        ArgumentNullException.ThrowIfNull(deltas);

        var h0 = skeleton;
        foreach (var delta in deltas.Where(d => d.Lifetime == DeltaLifetime.Permanent))
            h0 = ApplyConfigDelta(h0, delta);

        var current = h0;
        var rounds = deltas.Where(d => d.Lifetime == DeltaLifetime.Round).ToArray();
        foreach (var delta in rounds)
            current = ApplyConfigDelta(current, delta);

        return new CompositionHandle(h0, current, rounds.Length);
    }

    /// <summary>
    /// 安全组装：delta 目标在骨架不存在时 fail-closed 回退到干净态，绝不拖垮宿主启动。
    /// <para>对齐 HarnessComposer.ComposeSafe 语义，但改走统一 Delta 入口。</para>
    /// </summary>
    /// <param name="skeleton">骨架 profile。</param>
    /// <param name="deltas">本轮 delta。</param>
    /// <returns>可回滚句柄；任一 delta 不兼容时 Current 回退到 H₀。</returns>
    public static CompositionHandle ComposeSafe(
        ResolvedProfile skeleton,
        IReadOnlyList<CompositionDelta> deltas) {
        try {
            return Compose(skeleton, deltas);
        } catch (InvalidOperationException ex) {
            Console.Error.WriteLine(
                $"[DELTA] delta 与当前 profile '{skeleton.Name}' 不兼容，已忽略全部 delta 回退到干净态（DeltaComposer 安全组装层）。" +
                $" 原因: {ex.Message}" +
                " 正确写法: 让 delta 指向所有 profile 都保留的 consumer/node，或换用匹配的 profile。");
            return new CompositionHandle(skeleton, skeleton, 0);
        }
    }

    /// <summary>
    /// 唯一的键级覆写原语 — Permanent 与 Round 共用。
    /// <para>命中 consumer/node 时覆写单键（未提及键从 H₀ 保留 = 省略即继承）；
    /// 目标不存在时抛四段式 InvalidOperationException，由 ComposeSafe 兜底。</para>
    /// </summary>
    /// <param name="profile">被覆写的 profile。</param>
    /// <param name="delta">要叠加的 delta。</param>
    /// <returns>覆写后的新 profile（record with，不改原对象）。</returns>
    private static ResolvedProfile ApplyConfigDelta(ResolvedProfile profile, CompositionDelta delta) {
        if (!profile.Consumers.TryGetValue(delta.Consumer, out var consumer))
            throw new InvalidOperationException(
                $"delta 目标消费点 '{delta.Consumer}' 在 profile '{profile.Name}' 中不存在（DeltaComposer 合并层拦截）。" +
                $" 触发参数: consumer={delta.Consumer}, node={delta.Node}, key={delta.ConfigKey}, lifetime={delta.Lifetime}。" +
                $" 可用消费点: {string.Join(", ", profile.Consumers.Keys.Order())}。" +
                " 正确写法: 用骨架中已有的消费点 ID，或先在 profile YAML 增加该消费点。");

        if (consumer.Nodes.All(n => n.Id != delta.Node))
            throw new InvalidOperationException(
                $"delta 目标节点 '{delta.Node}' 在消费点 '{delta.Consumer}' 中不存在（DeltaComposer 合并层拦截）。" +
                $" 触发参数: consumer={delta.Consumer}, node={delta.Node}, key={delta.ConfigKey}, lifetime={delta.Lifetime}。" +
                $" 可用节点: {string.Join(", ", consumer.Nodes.Select(n => n.Id))}。" +
                " 正确写法: 指向骨架中已有的节点；delta 只覆写已有节点的 config 键，不能凭空新建节点。");

        var newNodes = consumer.Nodes.Select(node => {
            if (node.Id != delta.Node) return node;
            var config = new Dictionary<string, string>(node.Config, StringComparer.Ordinal) {
                [delta.ConfigKey] = delta.ConfigValue,
            };
            return node with { Config = config.ToFrozenDictionary(StringComparer.Ordinal) };
        }).ToArray();

        var newConsumers = new Dictionary<string, ResolvedConsumerConfig>(profile.Consumers, StringComparer.Ordinal) {
            [delta.Consumer] = consumer with { Nodes = newNodes },
        };
        return profile with { Consumers = newConsumers.ToFrozenDictionary(StringComparer.Ordinal) };
    }
}
