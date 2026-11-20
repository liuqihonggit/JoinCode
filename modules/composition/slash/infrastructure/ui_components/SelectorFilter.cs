namespace JoinCode.Cli;

/// <summary>
/// 选择器过滤（TASK069-C2 — 兑现 <see cref="Selector{T}"/> 构造的 `enableSearch` 参数）：
/// 命令行选择器没有 TUI 的 FuzzyMatcher，用大小写不敏感的子串匹配 —
/// 关键字按空白切成多个词，逐词都要命中搜索键（AND 语义，贴近用户「输入几个词缩小范围」的直觉）。
/// 纯函数，便于测试（ShowAsync 的终端交互不进单测）。
/// </summary>
public static class SelectorFilter {
    /// <summary>
    /// 按关键字过滤项元组；空白关键字返回原数组（引用不变 — 零分配快路径）。
    /// </summary>
    /// <typeparam name="T">项类型</typeparam>
    /// <param name="items">待过滤项元组（原始对象 / 显示文本 / 搜索键）</param>
    /// <param name="keyword">用户输入的关键字（可含空格分隔多个词）</param>
    public static (T Item, string DisplayText, string SearchKey)[] Apply<T>(
        (T Item, string DisplayText, string SearchKey)[] items, string? keyword) {
        if (string.IsNullOrWhiteSpace(keyword)) {
            return items;
        }

        var tokens = keyword.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0) {
            return items;
        }

        return [.. items.Where(item => tokens.All(token =>
            item.SearchKey.Contains(token, StringComparison.OrdinalIgnoreCase)))];
    }
}
