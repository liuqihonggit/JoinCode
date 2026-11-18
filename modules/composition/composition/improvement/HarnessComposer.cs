namespace JoinCode.Composition.Improvement;

/// <summary>
/// Harness 组装器 — 每轮从 H₀（公共基底 profile）全新解析，仅叠加显式 adopt 的 patch。
/// <para><b>H₀ 重置红线（结构性保证）</b>：<see cref="Compose"/> 的签名只接受「profiles + baseName + 本轮 patch」，
/// 根本不接受"上一轮的成品 profile"作参数 —— 想自动继承成品在类型层面就做不到。
/// 这正面回应 Harness² 论文实证：成品继承导致 Code 73.4→63.2 / Web 69.9→65.0 回退。</para>
/// </summary>
public static class HarnessComposer {
    /// <summary>
    /// 从所选 profile 的静态定义组装本轮 harness：无条件重新解析，再把 adopt 的 patch 逐条叠加。
    /// <para>"H₀" 指所选 profile 的干净初始态（每轮从 YAML 重新解析、绝不继承上一轮成品），
    /// 不一定固定是名为 "base" 的 profile —— 生产启动时 profileName 可能是 ci/sdk/headless。</para>
    /// </summary>
    /// <param name="profiles">全部 Profile 定义（来自 NodeProfileLoader，只读）。</param>
    /// <param name="profileName">本轮 profile 名称（每轮都从它的静态定义重新开始）。</param>
    /// <param name="adoptedPatches">本轮显式采纳的经验 patch；为空则结果严格等于该 profile 的干净态。</param>
    /// <returns>本轮解析后的 profile。</returns>
    /// <exception cref="InvalidOperationException">patch 目标 consumer/node 在该 profile 中不存在。</exception>
    public static ResolvedProfile Compose(
        FrozenDictionary<string, NodeProfile> profiles,
        string profileName,
        IReadOnlyList<HarnessPatch> adoptedPatches) {
        // 红线核心：永远重新 Resolve(profileName)，不接收也不缓存上一轮结果。
        var resolver = new NodeProfileResolver(profiles);
        var resolved = resolver.Resolve(profileName);

        foreach (var patch in adoptedPatches)
            resolved = ApplyPatch(resolved, patch);

        return resolved;
    }

    /// <summary>
    /// 安全组装：patch 目标在当前 profile 不存在时（如经验在 base 合法、但 ci 排除了该节点），
    /// 记录原因并 fail-closed 回退到干净 profile，绝不因一条不当经验拖垮宿主启动。
    /// </summary>
    /// <param name="profiles">全部 Profile 定义。</param>
    /// <param name="profileName">本轮 profile 名称。</param>
    /// <param name="adoptedPatches">本轮采纳的 patch。</param>
    /// <returns>组装后的 profile；patch 无效时为其干净初始态。</returns>
    public static ResolvedProfile ComposeSafe(
        FrozenDictionary<string, NodeProfile> profiles,
        string profileName,
        IReadOnlyList<HarnessPatch> adoptedPatches) {
        var resolver = new NodeProfileResolver(profiles);
        var clean = resolver.Resolve(profileName);
        if (adoptedPatches.Count == 0) return clean;
        try {
            var resolved = clean;
            foreach (var patch in adoptedPatches)
                resolved = ApplyPatch(resolved, patch);
            return resolved;
        } catch (InvalidOperationException ex) {
            Console.Error.WriteLine(
                $"[EXPERIENCE] 经验 patch 与当前 profile '{profileName}' 不兼容，已忽略全部 patch 回退到干净态（HarnessComposer 安全组装层）。" +
                $" 原因: {ex.Message}" +
                " 正确写法: 让经验 patch 指向所有 profile 都保留的节点，或换用匹配的 profile。");
            return clean;
        }
    }

    private static ResolvedProfile ApplyPatch(ResolvedProfile profile, HarnessPatch patch) {
        if (!profile.Consumers.TryGetValue(patch.Consumer, out var consumer))
            throw new InvalidOperationException(
                $"经验 patch 的目标消费点 '{patch.Consumer}' 在 H₀ 中不存在（HarnessComposer 组装层拦截）。" +
                $"触发参数: consumer={patch.Consumer}, node={patch.Node}。" +
                $"可用消费点: {string.Join(", ", profile.Consumers.Keys.Order())}。" +
                $"正确写法: 用 H₀ 中已有的消费点 ID，或先在 base.yaml 增加该消费点。");
        if (consumer.Nodes.All(n => n.Id != patch.Node))
            throw new InvalidOperationException(
                $"经验 patch 的目标节点 '{patch.Node}' 在消费点 '{patch.Consumer}' 的 H₀ 配置中不存在（HarnessComposer 组装层拦截）。" +
                $"触发参数: consumer={patch.Consumer}, node={patch.Node}, key={patch.ConfigKey}。" +
                $"可用节点: {string.Join(", ", consumer.Nodes.Select(n => n.Id))}。" +
                $"正确写法: 指向 H₀ 中已有的节点；经验只覆写已有节点 config，不能凭空新建节点。");

        var newNodes = consumer.Nodes.Select(node => {
            if (node.Id != patch.Node) return node;
            var config = new Dictionary<string, string>(node.Config, StringComparer.Ordinal) {
                [patch.ConfigKey] = patch.ConfigValue,
            };
            return node with { Config = config.ToFrozenDictionary(StringComparer.Ordinal) };
        }).ToArray();

        var newConsumers = new Dictionary<string, ResolvedConsumerConfig>(profile.Consumers, StringComparer.Ordinal) {
            [patch.Consumer] = consumer with { Nodes = newNodes },
        };
        return profile with { Consumers = newConsumers.ToFrozenDictionary(StringComparer.Ordinal) };
    }
}
