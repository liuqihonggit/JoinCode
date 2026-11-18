namespace JoinCode.Abstractions.Tools;

/// <summary>
/// 工具名等价表 — 「全名」与「短名」互为别名，另加一组高频同义词。
/// <para>存在理由：同一批文件工具在分组里长这样 —— <c>read</c> / <c>write</c> / <c>edit</c> 没有前缀，
/// 而 <c>file_delete</c> / <c>file_edit_regex</c> / <c>file_insert_lines</c> 有前缀。
/// 用户和模型都得先猜「这次要不要带 file_」，猜错就是「工具不存在」。命名该统一，但改 ID 会动到
/// 协议原生 tools 字段与内核注册表；更稳的做法是让两种写法同时成立。</para>
/// <para>判定分三层，顺序固定：① 精确（忽略大小写、忽略前导 <c>/</c>）② 组前缀互认
/// （<c>file_read</c> ↔ <c>read</c>，双向）③ 组内同义词（<c>rm</c> / <c>del</c> → 删文件）。
/// 同义词按组声明，不做全局表 —— 否则 <c>rm</c> 会去匹配 git 组或 github 组的某个东西。</para>
/// <para>本表是唯一数据源：宿主 <c>jcc tool call</c> 与各分组 exe 都调这里，不各写一份规则。</para>
/// </summary>
public static class ToolAliasTable {
    /// <summary>
    /// 组内同义词：组 ID → (用户可能敲的写法 → 该组里的短名)。
    /// <para>值必须是「短名」（不带组前缀），这样 <c>rm</c> 对 <c>delete</c> 与 <c>file_delete</c> 都成立。</para>
    /// </summary>
    private static readonly FrozenDictionary<string, FrozenDictionary<string, string>> GroupSynonyms =
        new Dictionary<string, FrozenDictionary<string, string>>(StringComparer.OrdinalIgnoreCase) {
            ["file"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {                // 删除文件 —— 各家的叫法都收下：shell 的 rm、Windows 的 del/erase、API 的 remove
                ["rm"] = "delete",
                ["del"] = "delete",
                ["remove"] = "delete",
                ["erase"] = "delete",
                ["unlink"] = "delete",
                // 读取
                ["cat"] = "read",
                ["type"] = "read",
                ["open"] = "read",
                // 整体写入
                ["save"] = "write",
                ["create"] = "write",
                ["new"] = "write",
                // 局部替换
                ["sed"] = "edit",
                ["replace"] = "edit",
                ["sub"] = "edit",
                // 目录浏览
                ["ls"] = "directory_list",
                ["dir"] = "directory_list",
                ["ll"] = "directory_list",
                // 补丁
                ["patch"] = "apply_patch"
            }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase)
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 把调用方给的名字解析成该组里已声明的规范 ID。
    /// </summary>
    /// <param name="groupId">分组 ID（如 <c>file</c>）。</param>
    /// <param name="declaredIds">该组实际声明的工具 ID 列表。</param>
    /// <param name="requested">调用方写的名字（可能是全名、短名或同义词）。</param>
    /// <returns>唯一命中的规范 ID；未命中或歧义时返回 <see langword="null"/>（不猜）。</returns>
    public static string? Resolve(string groupId, IReadOnlyList<string> declaredIds, string requested) {
        ArgumentNullException.ThrowIfNull(declaredIds);
        if (declaredIds.Count == 0 || string.IsNullOrWhiteSpace(requested)) return null;

        // ① 精确 —— 大小写与前导 '/' 都不该决定成败（斜杠组的 ID 写作 /agents）
        var exact = Match(declaredIds, requested);
        if (exact is not null) return exact;

        // ② 组前缀互认：file_read → read，read → file_read
        foreach (var candidate in ExpandPrefix(groupId, requested)) {
            var hit = Match(declaredIds, candidate);
            if (hit is not null) return hit;
        }

        // ③ 组内同义词：先试短名本身（组里可能就有不带前缀的 read），再补前缀（rm → delete → file_delete）
        if (SynonymsOf(groupId).TryGetValue(requested.TrimStart('/'), out var shortName)) {
            var direct = Match(declaredIds, shortName);
            if (direct is not null) return direct;
            foreach (var candidate in ExpandPrefix(groupId, shortName)) {
                var hit = Match(declaredIds, candidate);
                if (hit is not null) return hit;
            }
        }

        return null;
    }

    /// <summary>某个名字的全部等价写法（含自身），供 <c>jcc tool find</c> 之类的模糊检索使用。</summary>
    /// <param name="groupId">分组 ID。</param>
    /// <param name="toolId">规范 ID。</param>
    /// <returns>等价写法序列，第一项恒为规范 ID。</returns>
    public static IEnumerable<string> AliasesOf(string groupId, string toolId) {
        yield return toolId;

        var prefix = groupId + "_";
        var shortName = toolId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? toolId[prefix.Length..]
            : toolId;
        if (!string.Equals(shortName, toolId, StringComparison.OrdinalIgnoreCase))
            yield return shortName;
        else
            yield return prefix + toolId;

        foreach (var (word, target) in SynonymsOf(groupId)) {
            if (string.Equals(target, shortName, StringComparison.OrdinalIgnoreCase))
                yield return word;
        }
    }

    private static FrozenDictionary<string, string> SynonymsOf(string groupId) =>
        GroupSynonyms.TryGetValue(groupId, out var table)
            ? table
            : FrozenDictionary<string, string>.Empty;

    /// <summary>短名与全名两种写法 —— 名字带本组前缀就去掉，不带就补上。</summary>
    private static IEnumerable<string> ExpandPrefix(string groupId, string name) {
        var trimmed = name.TrimStart('/');
        var prefix = groupId + "_";
        if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) {
            var shortName = trimmed[prefix.Length..];
            if (shortName.Length > 0) yield return shortName;
        } else {
            yield return prefix + trimmed;
        }
    }

    /// <summary>在一组声明名里找唯一命中 —— 多个命中视为歧义，返回 null 交给上层报错。</summary>
    private static string? Match(IReadOnlyList<string> declaredIds, string name) {
        string? found = null;
        foreach (var declared in declaredIds) {
            if (!NameEquals(declared, name)) continue;
            if (found is not null) return null;
            found = declared;
        }
        return found;
    }

    private static bool NameEquals(string candidate, string name) =>
        string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase)
        || string.Equals(candidate.TrimStart('/'), name.TrimStart('/'), StringComparison.OrdinalIgnoreCase);
}
