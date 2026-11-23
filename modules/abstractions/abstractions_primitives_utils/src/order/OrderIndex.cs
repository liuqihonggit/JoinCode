namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 注册顺序索引 — 「谁先执行」的唯一数据源。
/// <para>
/// 数据源是 <c>resources/orders.yaml</c>（内嵌资源）。每个列表是一串注册名，
/// <b>列表内顺序即执行顺序</b>（第一条先执行）。
/// </para>
/// <para>
/// 为什么不再用数字：<c>Priority</c> / <c>Order</c> 这类整数只能表达「相对大小」，
/// 读代码想知道谁先执行，必须把所有实现的数字横向比对一遍；而数字与注册名是<b>两套标识</b>，
/// 改个类名要同步改数字、改个数字要重新确认全序。八个命令守卫用了
/// 30 / 50 / 100 / 200 / 500 / 800 / 900 / 1000 八个离散值，中间的差值毫无语义
/// ——只有相对顺序有意义，数字本身是冗余且易漂移的。
/// </para>
/// <para>
/// 未登记的实现会直接抛异常而不是静默兜底：命令守卫链尤其不能「按未知顺序执行」。
/// </para>
/// </summary>
public sealed class OrderIndex {
    /// <summary>内嵌资源名 — 与 <c>Abstractions.Primitives.Utils.csproj</c> 的 LogicalName 一致。</summary>
    private const string ResourceName = "JoinCode.Abstractions.Utils.orders.yaml";

    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _lists;

    private OrderIndex(IReadOnlyDictionary<string, IReadOnlyList<string>> lists) {
        _lists = lists;
    }

    /// <summary>默认实例 — 从内嵌的 orders.yaml 加载。</summary>
    public static OrderIndex Default { get; } = LoadDefault();

    /// <summary>全部列表名。</summary>
    public IEnumerable<string> ListNames => _lists.Keys;

    /// <summary>取某个列表的注册名顺序（空列表返回空数组）。</summary>
    /// <param name="listName">列表名，如 <c>command_guards</c></param>
    public IReadOnlyList<string> Get(string listName) =>
        _lists.TryGetValue(listName, out var items) ? items : [];

    /// <summary>
    /// 取注册名在列表中的位置 — 未登记返回 <see cref="int.MaxValue"/>（排到最后）。
    /// 只用于「允许缺席」的场合；守卫链这类必须确定的场景用 <see cref="Require"/>。
    /// </summary>
    public int IndexOf(string listName, string itemName) {
        var items = Get(listName);
        for (var i = 0; i < items.Count; i++) {
            if (string.Equals(items[i], itemName, StringComparison.Ordinal)) return i;
        }
        return int.MaxValue;
    }

    /// <summary>
    /// 取注册名在列表中的位置 — 未登记直接抛异常。
    /// </summary>
    /// <param name="listName">列表名，如 <c>command_guards</c></param>
    /// <param name="itemName">注册名（实现类名）</param>
    /// <exception cref="InvalidOperationException">该列表没有登记这个注册名</exception>
    public int Require(string listName, string itemName) {
        var index = IndexOf(listName, itemName);
        if (index != int.MaxValue) return index;

        var known = Get(listName);
        var knownText = known.Count == 0 ? "（该列表当前是空的）" : string.Join(", ", known);
        throw new InvalidOperationException(
            $"注册顺序表 '{listName}' 没有登记 '{itemName}'（OrderIndex.Require 拦截）。" +
            "执行顺序是显式设计：实现类里不再允许写 Priority / Order 数字，" +
            $"新增实现必须在 resources/orders.yaml 的 '{listName}' 列表里登记，" +
            "否则无法确定它与其它实现的先后关系——守卫链尤其不能按未知顺序执行。" +
            $"当前 '{listName}' 已登记：{knownText}。");
    }

    /// <summary>
    /// 取注册名的「越大越先执行」位次值 —— 列表第一项得到列表长度，最后一项得到 1。
    /// <para>用于历史上以 <c>Priority</c> 表达、且约定「数字大 = 先执行」的注册表
    /// （命令守卫链、命令拦截器链、工具修正 hook）。</para>
    /// </summary>
    /// <param name="listName">列表名，如 <c>command_guards</c></param>
    /// <param name="itemName">注册名（实现类名）</param>
    /// <exception cref="InvalidOperationException">该列表没有登记这个注册名</exception>
    public int PriorityOf(string listName, string itemName) {
        var index = Require(listName, itemName);
        return Get(listName).Count - index;
    }

    /// <summary>
    /// 取注册名的「越小越先执行」位次值 —— 列表第一项得到 1，依次递增。
    /// <para>用于历史上以 <c>Order</c> / <c>Priority</c> 表达、且约定「数字小 = 先执行」的注册表
    /// （Agent 工具中间件、查询停止 hook）。</para>
    /// </summary>
    /// <param name="listName">列表名，如 <c>agent_tool_middlewares</c></param>
    /// <param name="itemName">注册名（实现类名）</param>
    /// <exception cref="InvalidOperationException">该列表没有登记这个注册名</exception>
    public int OrderOf(string listName, string itemName) => Require(listName, itemName) + 1;

    /// <summary>从内嵌资源加载。</summary>
    public static OrderIndex LoadDefault() {
        var assembly = typeof(OrderIndex).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"未找到内嵌资源 '{ResourceName}'（OrderIndex 加载层拦截）：请检查 Abstractions.Primitives.Utils.csproj 的 EmbeddedResource 声明");
        using var reader = new StreamReader(stream);
        return FromText(reader.ReadToEnd(), ResourceName);
    }

    /// <summary>从 YAML 文本加载（测试与对账脚本用）。</summary>
    /// <param name="yaml">YAML 文本</param>
    /// <param name="source">来源标识，出现在错误信息里</param>
    public static OrderIndex FromText(string yaml, string source = "<text>") {
        var root = ParseRoot(yaml, source);
        var listsNode = root.Children.TryGetValue("lists", out var found) ? found : null;
        if (listsNode is not YamlMappingNode lists) {
            throw new InvalidOperationException($"注册顺序表 '{source}' 缺少 lists 映射（OrderIndex 解析层拦截）");
        }

        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in lists.Children) {
            var listName = (pair.Key as YamlScalarNode)?.Value;
            if (string.IsNullOrWhiteSpace(listName)) {
                throw new InvalidOperationException($"注册顺序表 '{source}' 存在无名列表（OrderIndex 解析层拦截）");
            }
            if (pair.Value is not YamlSequenceNode sequence) {
                throw new InvalidOperationException(
                    $"注册顺序表 '{source}' 的列表 '{listName}' 必须是序列（OrderIndex 解析层拦截）");
            }
            var names = sequence.Children
                .OfType<YamlScalarNode>()
                .Select(node => node.Value ?? string.Empty)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToArray();
            var duplicated = names
                .GroupBy(name => name, StringComparer.Ordinal)
                .FirstOrDefault(group => group.Count() > 1);
            if (duplicated is not null) {
                throw new InvalidOperationException(
                    $"注册顺序表 '{source}' 的列表 '{listName}' 重复登记了 '{duplicated.Key}'（OrderIndex 解析层拦截）");
            }
            result[listName] = names;
        }
        return new OrderIndex(result);
    }

    private static YamlMappingNode ParseRoot(string yaml, string source) {
        var stream = new YamlStream();
        try {
            stream.Load(new StringReader(yaml));
        } catch (YamlException ex) {
            throw new InvalidOperationException($"注册顺序表 '{source}' YAML 解析失败: {ex.Message}", ex);
        }
        if (stream.Documents.Count == 0) {
            throw new InvalidOperationException($"注册顺序表 '{source}' 为空（OrderIndex 解析层拦截）");
        }
        if (stream.Documents[0].RootNode is not YamlMappingNode root) {
            throw new InvalidOperationException($"注册顺序表 '{source}' 根节点必须是映射（OrderIndex 解析层拦截）");
        }
        return root;
    }
}
