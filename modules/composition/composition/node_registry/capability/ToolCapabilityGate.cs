namespace JoinCode.Composition.NodeRegistry;

/// <summary>
/// 能力 → 工具分类映射 — 回答「关掉这项能力，哪些工具不该再出现」。
/// <para>
/// TASK100 S4 收尾：此前 <c>capabilities</c> 只门禁「服务解析」与「插件加载」，
/// 工具表仍是无条件全量注册 —— 于是 <c>profile</c> 里去掉 <c>todo</c>，
/// <c>ITodoService</c> 确实为 <see langword="null"/>，但 <c>todo</c> 工具还挂在工具表上，
/// 调用时才发现服务不在。这是「只关一半」的典型症状：配置看起来生效了，实际只生效在某一层。
/// </para>
/// <para>
/// 映射的键是 <see cref="JoinCode.Abstractions.Utils.ToolCategory"/> 的 <c>[EnumValue]</c> 字符串
/// （即生成器算出的 <c>tool.Category ?? handler.DisplayName</c>），与
/// <c>GetAvailableToolCategories()</c> 的字典键同一坐标系 —— 不引入第二套分类词汇。
/// </para>
/// <para>
/// 只登记「真有能力治理的工具分类」：<c>dream</c> 没有对应工具分类（Dream 是引擎后台任务，
/// 不暴露工具），所以它不在此表中，关掉它不会改变工具表 —— 这一点写在文档里，不写成假映射。
/// </para>
/// </summary>
public static class CapabilityToolCategories {
    /// <summary>工具分类名 → 治理它的能力节点 ID（一个分类可被多个能力共同治理）。</summary>
    public static IReadOnlyDictionary<string, string[]> ByCategory { get; } =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase) {
            ["goal"] = [CapabilityNodes.Goal],
            ["todo"] = [CapabilityNodes.Todo],
            ["task"] = [CapabilityNodes.Task],
            ["cron"] = [CapabilityNodes.Cron],
            ["memory"] = [CapabilityNodes.Memdir],
            ["code_index"] = [CapabilityNodes.CodeIndex]
        };

    /// <summary>受能力治理的工具分类数量（诊断用）。</summary>
    public static int GovernedCategoryCount => ByCategory.Count;

    /// <summary>工具名 → 分类名（来自源码生成器，全量、无重复）。</summary>
    public static FrozenDictionary<string, string> CategoryByTool { get; } = BuildCategoryIndex();

    /// <summary>能力节点 ID → 它一关闭就会被裁掉的工具数（供 <c>--dump-nodes</c> 展示治理面）。</summary>
    private static IReadOnlyDictionary<string, int> GovernedToolCounts { get; } = BuildToolCounts();

    /// <summary>
    /// 取某能力治理的工具数量 —— 数量为 0 表示该能力不治理工具面（如 <c>dream</c>）。
    /// </summary>
    /// <param name="capabilityId">能力节点 ID。</param>
    /// <returns>工具数量。</returns>
    public static int GovernedToolCount(string capabilityId) =>
        GovernedToolCounts.TryGetValue(capabilityId, out var count) ? count : 0;

    private static FrozenDictionary<string, string> BuildCategoryIndex() {
        var categories = GeneratedToolHandlerRegistration_JoinCode_Composition.GetAvailableToolCategories();
        var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in categories) {
            foreach (var entry in pair.Value)
                index.TryAdd(entry.Name, pair.Key);
        }
        return index.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyDictionary<string, int> BuildToolCounts() {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var index = CategoryByTool;
        foreach (var pair in index) {
            if (!ByCategory.TryGetValue(pair.Value, out var owners))
                continue;
            foreach (var capability in owners)
                counts[capability] = counts.GetValueOrDefault(capability) + 1;
        }
        return counts;
    }
}

/// <summary>
/// 工具注册门禁 — 按当前 profile 的能力声明决定「某个工具是否注册」。
/// <para>
/// 与 <see cref="CapabilitySet"/> 的分工：后者回答「服务/插件在不在」，本类回答「工具在不在」，
/// 二者共用同一条 <c>capabilities</c> 声明（不新增消费点、不新增 profile 词汇）。
/// </para>
/// <para>
/// 三条保守规则，避免门禁变成「静默少工具」：
/// </para>
/// <list type="number">
/// <item>生成物里查不到的工具 → 放行（宁可多给，不可误杀）。</item>
/// <item>分类不归任何能力治理 → 放行。</item>
/// <item>分类被多个能力共享 → 任一能力开启即放行。</item>
/// </list>
/// </summary>
[Register(typeof(ToolCapabilityGate), ServiceLifetime.Singleton)]
public sealed class ToolCapabilityGate {
    private readonly CapabilitySet _capabilities;
    private readonly FrozenDictionary<string, string> _categoryByTool;
    private readonly FrozenDictionary<string, string[]> _capabilitiesByCategory;

    /// <summary>
    /// 构造门禁。
    /// </summary>
    /// <param name="capabilities">能力视图；为 <see langword="null"/> 时视为全开（轻量宿主的历史行为）。</param>
    public ToolCapabilityGate(CapabilitySet? capabilities = null) {
        _capabilities = capabilities ?? CapabilitySet.AllEnabled;
        _categoryByTool = CapabilityToolCategories.CategoryByTool;
        _capabilitiesByCategory = CapabilityToolCategories.ByCategory.ToFrozenDictionary(
            static pair => pair.Key,
            static pair => pair.Value,
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>被索引的工具数量（= 生成器看到的工具数）。</summary>
    public int IndexedToolCount => _categoryByTool.Count;

    /// <summary>
    /// 当前 profile 是否真的会裁掉某些工具 —— 为 <see langword="false"/> 时调用方应保持
    /// <c>toolFilter</c> 为 <see langword="null"/>，让生成代码走「不过滤」的快路径。
    /// </summary>
    public bool HasActiveFilter {
        get {
            foreach (var capability in AllGovernedCapabilities()) {
                if (!_capabilities.IsEnabled(capability))
                    return true;
            }
            return false;
        }
    }

    /// <summary>
    /// 判断某工具在当前 profile 是否应注册。
    /// </summary>
    /// <param name="toolName">工具名。</param>
    /// <returns>应注册返回 <see langword="true"/>。</returns>
    public bool IsToolEnabled(string toolName) {
        if (string.IsNullOrEmpty(toolName))
            return true;

        if (!_categoryByTool.TryGetValue(toolName, out var category))
            return true;

        if (!_capabilitiesByCategory.TryGetValue(category, out var owners))
            return true;

        foreach (var capability in owners) {
            if (_capabilities.IsEnabled(capability))
                return true;
        }
        return false;
    }

    /// <summary>
    /// 生成一行诊断文本（<c>[TOOL-GATE] indexed=635 off=1 blocked=13</c>）——
    /// 「工具到底少了没」必须能一眼看到，而不是靠数工具树。
    /// </summary>
    /// <returns>诊断文本；无裁剪时给出 <c>off=0 blocked=0</c>。</returns>
    public string Describe() {
        var off = new List<string>();
        foreach (var capability in AllGovernedCapabilities()) {
            if (!_capabilities.IsEnabled(capability))
                off.Add(capability);
        }

        var offText = off.Count == 0 ? "-" : string.Join(',', off);
        var sampleText = off.Count == 0 ? "-" : string.Join(',', CollectBlockedSamples());
        return $"indexed={_categoryByTool.Count} off=[{offText}] blocked={CountBlocked()} samples=[{sampleText}]";
    }

    private const int SampleLimit = 6;

    /// <summary>
    /// 取前几个被裁掉的工具名 —— 「少了哪几个」是排查时第一个问题，
    /// 但把 59 个工具名铺满一行会让诊断失去可读性。
    /// </summary>
    /// <returns>至多 <see cref="SampleLimit"/> 个工具名。</returns>
    private List<string> CollectBlockedSamples() {
        var samples = new List<string>();
        foreach (var pair in _categoryByTool) {
            if (!IsBlockedCategory(pair.Value))
                continue;
            if (samples.Count >= SampleLimit)
                break;
            samples.Add(pair.Key);
        }
        return samples;
    }

    private int CountBlocked() {
        var blocked = 0;
        foreach (var pair in _categoryByTool) {
            if (IsBlockedCategory(pair.Value))
                blocked++;
        }
        return blocked;
    }

    private bool IsBlockedCategory(string category) =>
        _capabilitiesByCategory.TryGetValue(category, out var owners) && !IsAnyEnabled(owners);

    private bool IsAnyEnabled(string[] owners) {
        foreach (var capability in owners) {
            if (_capabilities.IsEnabled(capability))
                return true;
        }
        return false;
    }

    private static IEnumerable<string> AllGovernedCapabilities() {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var owners in CapabilityToolCategories.ByCategory.Values) {
            foreach (var capability in owners) {
                if (seen.Add(capability))
                    yield return capability;
            }
        }
    }
}
