namespace JccAuditCli;

/// <summary>
/// 错误行匹配器 — 统一"节点是否在错误位置"判断，支持精确行、精确行+错误码、范围相交三种查询。
/// 内部用有序数组 + 二分查找实现范围查询，O(log n) 替代 HashSet.Any 的 O(n) 线性扫描。
/// 替代原 CascadeRewriter._errorLines（HashSet+线性扫描）与 UnawaitedVariableRewriter._errorMap（Dictionary）两套不一致抽象。
/// </summary>
internal sealed class ErrorLineMatcher {
    private readonly Dictionary<int, string> _lineToCode;
    private readonly int[] _sortedLines;

    /// <summary>
    /// 从错误列表构造匹配器。同一行多个错误保留最后一个错误码。
    /// </summary>
    /// <param name="errors">错误序列（行号, 错误码）</param>
    internal ErrorLineMatcher(IEnumerable<(int Line, string Code)> errors) {
        _lineToCode = new Dictionary<int, string>();
        foreach (var (line, code) in errors)
            _lineToCode[line] = code;
        _sortedLines = _lineToCode.Keys.Order().ToArray();
    }

    /// <summary>
    /// 精确行判断 — 该行是否为错误行。
    /// </summary>
    internal bool Contains(int line) => _lineToCode.ContainsKey(line);

    /// <summary>
    /// 精确行 + 错误码判断 — 该行是否为指定错误码的错误行。
    /// </summary>
    internal bool Contains(int line, string code) =>
        _lineToCode.TryGetValue(line, out var c) && c == code;

    /// <summary>
    /// 范围相交判断 — 是否存在错误行落在 [start, end] 区间内。
    /// 用二分查找定位第一个 >= start 的错误行，检查其是否 <= end。O(log n)。
    /// </summary>
    internal bool IntersectsRange(int start, int end) {
        if (_sortedLines.Length == 0) return false;
        var idx = Array.BinarySearch(_sortedLines, start);
        if (idx < 0) idx = ~idx;
        return idx < _sortedLines.Length && _sortedLines[idx] <= end;
    }
}
