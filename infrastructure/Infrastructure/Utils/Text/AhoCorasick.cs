namespace Infrastructure.Utils.Text;

/// <summary>
/// Aho-Corasick 多模式串匹配结果。
/// </summary>
/// <typeparam name="TValue">模式关联值类型。</typeparam>
/// <param name="StartIndex">匹配起始位置(在输入文本中的 0-based 索引)。</param>
/// <param name="Length">匹配的模式串长度。</param>
/// <param name="Value">模式关联值。</param>
public readonly record struct AcMatch<TValue>(int StartIndex, int Length, TValue Value);

/// <summary>
/// 泛型 Aho-Corasick 自动机 — 多模式串匹配 DFA。
/// 构建后不可变,线程安全。O(n) 扫描文本同时匹配所有模式串。
/// 适用于敏感词过滤、黑名单检测、关键字注入等场景。
/// </summary>
/// <typeparam name="TValue">每个模式串关联的值类型。</typeparam>
public sealed class AhoCorasick<TValue>
{
    private readonly Dictionary<char, int>[] _transitions;
    private readonly int[] _failures;
    private readonly AcOutput[][] _outputs;
    private readonly bool _ignoreCase;

    private AhoCorasick(
        Dictionary<char, int>[] transitions,
        int[] failures,
        AcOutput[][] outputs,
        bool ignoreCase)
    {
        _transitions = transitions;
        _failures = failures;
        _outputs = outputs;
        _ignoreCase = ignoreCase;
    }

    private static readonly AhoCorasick<TValue> EmptyIgnoreCase = new(
        [new Dictionary<char, int>()], [0],
        [Array.Empty<AcOutput>()], true);

    private static readonly AhoCorasick<TValue> EmptyOrdinal = new(
        [new Dictionary<char, int>()], [0],
        [Array.Empty<AcOutput>()], false);

    /// <summary>
    /// 从模式集合构建自动机。
    /// </summary>
    /// <param name="patterns">模式串 → 关联值 的键值对集合。</param>
    /// <param name="ignoreCase">是否忽略大小写(默认 true)。</param>
    /// <returns>构建完成的不可变自动机。</returns>
    public static AhoCorasick<TValue> Create(
        IEnumerable<KeyValuePair<string, TValue>> patterns,
        bool ignoreCase = true)
    {
        if (patterns is null)
            return ignoreCase ? EmptyIgnoreCase : EmptyOrdinal;

        var patternList = patterns as IList<KeyValuePair<string, TValue>>
            ?? patterns.ToList();

        if (patternList.Count == 0)
            return ignoreCase ? EmptyIgnoreCase : EmptyOrdinal;

        return Build(patternList, ignoreCase);
    }

    private static AhoCorasick<TValue> Build(
        IList<KeyValuePair<string, TValue>> patterns,
        bool ignoreCase)
    {
        var transitions = new List<Dictionary<char, int>> { new() };
        var outputs = new List<List<AcOutput>> { new() };
        var hasPattern = false;

        foreach (var pair in patterns)
        {
            var pattern = pair.Key;
            if (string.IsNullOrEmpty(pattern)) continue;
            hasPattern = true;

            var state = 0;
            for (var i = 0; i < pattern.Length; i++)
            {
                var c = ignoreCase ? char.ToLowerInvariant(pattern[i]) : pattern[i];
                if (!transitions[state].TryGetValue(c, out var next))
                {
                    next = transitions.Count;
                    transitions.Add(new Dictionary<char, int>());
                    outputs.Add(new List<AcOutput>());
                    transitions[state][c] = next;
                }
                state = next;
            }
            outputs[state].Add(new AcOutput(pattern.Length, pair.Value));
        }

        if (!hasPattern)
            return ignoreCase ? EmptyIgnoreCase : EmptyOrdinal;

        var stateCount = transitions.Count;
        var failures = new int[stateCount];
        var queue = new Queue<int>();

        foreach (var (_, child) in transitions[0])
        {
            failures[child] = 0;
            queue.Enqueue(child);
        }

        while (queue.Count > 0)
        {
            var state = queue.Dequeue();
            foreach (var (c, child) in transitions[state])
            {
                queue.Enqueue(child);

                var f = failures[state];
                while (f != 0 && !transitions[f].ContainsKey(c))
                    f = failures[f];

                if (transitions[f].TryGetValue(c, out var failTarget) && failTarget != child)
                    failures[child] = failTarget;
                else
                    failures[child] = 0;

                var failState = failures[child];
                if (outputs[failState].Count > 0)
                    outputs[child].AddRange(outputs[failState]);
            }
        }

        var transitionsArray = transitions.ToArray();
        var outputsArray = new AcOutput[stateCount][];
        for (var i = 0; i < stateCount; i++)
            outputsArray[i] = outputs[i].ToArray();

        return new AhoCorasick<TValue>(transitionsArray, failures, outputsArray, ignoreCase);
    }

    /// <summary>
    /// 快速判断文本是否包含任意模式。找到第一个匹配立即返回。
    /// </summary>
    public bool ContainsAny(ReadOnlySpan<char> text)
    {
        var state = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = _ignoreCase ? char.ToLowerInvariant(text[i]) : text[i];
            while (state != 0 && !_transitions[state].TryGetValue(c, out _))
                state = _failures[state];
            _transitions[state].TryGetValue(c, out state);
            if (_outputs[state].Length > 0)
                return true;
        }
        return false;
    }

    /// <summary>
    /// 查找文本中所有匹配,按出现位置排序。
    /// </summary>
    public List<AcMatch<TValue>> FindAll(ReadOnlySpan<char> text)
    {
        var results = new List<AcMatch<TValue>>();
        var state = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = _ignoreCase ? char.ToLowerInvariant(text[i]) : text[i];
            while (state != 0 && !_transitions[state].TryGetValue(c, out _))
                state = _failures[state];
            _transitions[state].TryGetValue(c, out state);
            var outputs = _outputs[state];
            if (outputs.Length > 0)
            {
                for (var j = 0; j < outputs.Length; j++)
                {
                    ref var o = ref outputs[j];
                    results.Add(new AcMatch<TValue>(i - o.Length + 1, o.Length, o.Value));
                }
            }
        }
        return results;
    }

    /// <summary>
    /// 查找第一个匹配,无匹配返回 null。
    /// </summary>
    public AcMatch<TValue>? FindFirst(ReadOnlySpan<char> text)
    {
        var state = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = _ignoreCase ? char.ToLowerInvariant(text[i]) : text[i];
            while (state != 0 && !_transitions[state].TryGetValue(c, out _))
                state = _failures[state];
            _transitions[state].TryGetValue(c, out state);
            var outputs = _outputs[state];
            if (outputs.Length > 0)
            {
                ref var o = ref outputs[0];
                return new AcMatch<TValue>(i - o.Length + 1, o.Length, o.Value);
            }
        }
        return null;
    }

    internal readonly record struct AcOutput(int Length, TValue Value);
}

/// <summary>
/// Aho-Corasick 自动机便捷工厂。
/// </summary>
public static class AhoCorasick
{
    /// <summary>从模式集合构建自动机,模式本身作为关联值。</summary>
    public static AhoCorasick<string> Create(IEnumerable<string> patterns, bool ignoreCase = true)
    {
        return AhoCorasick<string>.Create(
            patterns.Select(static p => new KeyValuePair<string, string>(p, p)),
            ignoreCase);
    }

    /// <summary>从模式集合构建自动机,用 bool true 标记命中。</summary>
    public static AhoCorasick<bool> CreateBool(IEnumerable<string> patterns, bool ignoreCase = true)
    {
        return AhoCorasick<bool>.Create(
            patterns.Select(static p => new KeyValuePair<string, bool>(p, true)),
            ignoreCase);
    }
}
