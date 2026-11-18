namespace JoinCode.Abstractions.Tools;

/// <summary>
/// CLI 节点「省略即继承」解析器 —— 子 node.yaml 只声明差异，未提及的字段从父节点继承。
/// <para>期望值来源：Harness² 的 Tree dict / write_tree 语义（DSG048 §2.2.2）——子只写出要改的文件，
/// 未提及的从 base 继承；迁移到节点层即「子只声明差异字段，未提及的标量/数组从父继承，
/// parameters/tools 按 name/id 合并（子覆盖同名）」。</para>
/// <para>刻意停留在零依赖的 abstractions 层，与 composition 层 NodeProfileResolver 的合并语义一致，
/// 但不反向引用 composition —— 13 个分组 exe 靠这条依赖墙保持独立。</para>
/// </summary>
public static class CliNodeInheritanceResolver {
    /// <summary>继承链最大节点数（含自身）——与 NodeProfileResolver 的深度上限对齐。</summary>
    private const int MaxDepth = 3;

    /// <summary>
    /// 解析单节点继承链。<see cref="CliToolDescriptor.Inherits"/> 为 null 时原样返回（绝大多数节点走这条路）。
    /// </summary>
    /// <param name="node">待解析节点。</param>
    /// <param name="pool">可供查找父节点的节点池（通常是同一扫描目录下的全部 node.yaml）。</param>
    /// <exception cref="InvalidOperationException">父缺失 / 成环 / 超深 —— 错误信息含原因、触发节点、拦截层与正确写法。</exception>
    public static CliToolDescriptor Resolve(CliToolDescriptor node, IReadOnlyList<CliToolDescriptor> pool) {
        if (node.Inherits is null) return node;
        return ResolveChain(node, pool, []);
    }

    /// <summary>
    /// 批量解析：目录扫描后调用。单个节点继承失败不拖垮整棵树 —— stderr 报错并保留原节点，
    /// 让下钻仍能看见该工具，只是没继承到父属性（与 NodeManifestLoader 的损坏清单容错一致）。
    /// </summary>
    public static IReadOnlyList<CliToolDescriptor> ResolveAll(IEnumerable<CliToolDescriptor> nodes) {
        var pool = nodes.ToArray();
        var result = new List<CliToolDescriptor>(pool.Length);
        foreach (var node in pool) {
            try {
                result.Add(Resolve(node, pool));
            } catch (InvalidOperationException ex) {
                Console.Error.WriteLine(
                    $"保留未解析节点 '{node.Id}'（CliNodeInheritanceResolver 目录扫描层拦截）：{ex.Message}");
                result.Add(node);
            }
        }
        return result;
    }

    private static CliToolDescriptor ResolveChain(
        CliToolDescriptor node, IReadOnlyList<CliToolDescriptor> pool, List<string> chain) {
        if (chain.Any(id => string.Equals(id, node.Id, StringComparison.OrdinalIgnoreCase))) {
            throw new InvalidOperationException(
                $"CLI 节点继承链存在循环: {string.Join(" -> ", chain.Append(node.Id))}。" +
                "CliNodeInheritanceResolver 拒绝解析（inherits 链不能成环）。" +
                $"触发节点: '{node.Id}'（inherits='{node.Inherits}'）。" +
                "正确写法: 断开环上任意一个 inherits，或让它指向无环的父节点。");
        }
        if (chain.Count >= MaxDepth) {
            throw new InvalidOperationException(
                $"CLI 节点继承链深度超过 {MaxDepth}: {string.Join(" -> ", chain.Append(node.Id))}。" +
                "CliNodeInheritanceResolver 在继承解析层拦截（过深链条难审计）。" +
                "正确写法: 把公共字段上提到根父节点，缩短链条。");
        }
        if (node.Inherits is null) return node;

        chain.Add(node.Id);
        var parent = pool.FirstOrDefault(p => string.Equals(p.Id, node.Inherits, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"CLI 节点 '{node.Id}' 声明 inherits='{node.Inherits}'，" +
                $"但在提供的 {pool.Count} 个节点中找不到该父节点。" +
                "CliNodeInheritanceResolver 在继承解析层拦截（不会静默忽略继承）。" +
                $"正确写法: 把 '{node.Inherits}' 的 node.yaml 放进同一扫描目录，或移除 '{node.Id}' 的 inherits 字段。");

        var resolvedParent = ResolveChain(parent, pool, chain);
        return Merge(node, resolvedParent);
    }

    private static CliToolDescriptor Merge(CliToolDescriptor child, CliToolDescriptor parent) => new() {
        Id = Pick(child, parent, "id", n => n.Id),
        Name = Pick(child, parent, "name", n => n.Name),
        Summary = Pick(child, parent, "summary", n => n.Summary),
        Category = Pick(child, parent, "category", n => n.Category),
        Inherits = child.Inherits,
        Exe = Pick(child, parent, "exe", n => n.Exe),
        Tags = Pick(child, parent, "tags", n => n.Tags),
        Examples = Pick(child, parent, "examples", n => n.Examples),
        Parameters = IsDeclared(child, "parameters")
            ? MergeParameters(child.Parameters, parent.Parameters)
            : parent.Parameters,
        Tools = IsDeclared(child, "tools")
            ? MergeTools(child.Tools, parent.Tools)
            : parent.Tools,
        DeclaredKeys = child.DeclaredKeys
    };

    private static T Pick<T>(
        CliToolDescriptor child, CliToolDescriptor parent, string key, Func<CliToolDescriptor, T> getter) =>
        IsDeclared(child, key) ? getter(child) : getter(parent);

    /// <summary>字段是否由子显式声明。手工构造（DeclaredKeys 空）视为全部显式。</summary>
    private static bool IsDeclared(CliToolDescriptor node, string key) =>
        node.DeclaredKeys.Count == 0 || node.DeclaredKeys.Contains(key);

    /// <summary>参数按 name 合并：父打底，子同名整体覆写，子新增追加。</summary>
    private static CliToolParameter[] MergeParameters(CliToolParameter[] child, CliToolParameter[] parent) {
        var result = new List<CliToolParameter>(parent);
        foreach (var param in child) {
            var index = result.FindIndex(p => string.Equals(p.Name, param.Name, StringComparison.Ordinal));
            if (index >= 0) result[index] = param;
            else result.Add(param);
        }
        return [.. result];
    }

    /// <summary>子工具按 id 合并：父打底，子同名整体覆写，子新增追加。</summary>
    private static CliToolEntry[] MergeTools(CliToolEntry[] child, CliToolEntry[] parent) {
        var result = new List<CliToolEntry>(parent);
        foreach (var tool in child) {
            var index = result.FindIndex(t => string.Equals(t.Id, tool.Id, StringComparison.OrdinalIgnoreCase));
            if (index >= 0) result[index] = tool;
            else result.Add(tool);
        }
        return [.. result];
    }
}
