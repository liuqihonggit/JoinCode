namespace Infrastructure.Utils.Text;

/// <summary>
/// 双缓冲 Aho-Corasick 自动机 — 支持模式集热更新。
/// 维护 _active 自动机实例,更新时构建新自动机后原子交换。
/// 读端无锁,适合高频读取、低频更新的场景。
/// 对齐架构规则3:双变量切换模式 — _staging 验证后原子交换 _active。
/// </summary>
/// <typeparam name="TValue">每个模式串关联的值类型。</typeparam>
public sealed class DualBufferAhoCorasick<TValue>
{
    private AhoCorasick<TValue> _active;
    private readonly bool _ignoreCase;

    /// <summary>
    /// 从初始自动机创建双缓冲包装(复用已构建的自动机作为初始 _active)。
    /// </summary>
    public DualBufferAhoCorasick(AhoCorasick<TValue> initial, bool ignoreCase = true)
    {
        _active = initial;
        _ignoreCase = ignoreCase;
    }

    /// <summary>
    /// 从初始模式集合创建双缓冲自动机。
    /// </summary>
    /// <param name="initialPatterns">初始模式串 → 关联值 的键值对集合。</param>
    /// <param name="ignoreCase">是否忽略大小写(默认 true)。</param>
    public DualBufferAhoCorasick(
        IEnumerable<KeyValuePair<string, TValue>> initialPatterns,
        bool ignoreCase = true)
    {
        _ignoreCase = ignoreCase;
        _active = AhoCorasick<TValue>.Create(initialPatterns, ignoreCase);
    }

    /// <summary>
    /// 当前生效的自动机(读端无锁,返回最新引用)。
    /// </summary>
    public AhoCorasick<TValue> Current => Volatile.Read(ref _active);

    /// <summary>
    /// 原子切换模式集。构建新自动机后替换 _active,旧自动机由 GC 回收。
    /// </summary>
    public void SwapPatterns(IEnumerable<KeyValuePair<string, TValue>> newPatterns)
    {
        var staging = AhoCorasick<TValue>.Create(newPatterns, _ignoreCase);
        Interlocked.Exchange(ref _active, staging);
    }

    /// <summary>快速判断文本是否包含任意模式。</summary>
    public bool ContainsAny(ReadOnlySpan<char> text) => Current.ContainsAny(text);

    /// <summary>查找文本中所有匹配。</summary>
    public List<AcMatch<TValue>> FindAll(ReadOnlySpan<char> text) => Current.FindAll(text);

    /// <summary>查找第一个匹配。</summary>
    public AcMatch<TValue>? FindFirst(ReadOnlySpan<char> text) => Current.FindFirst(text);
}

/// <summary>
/// 双缓冲 Aho-Corasick 便捷工厂(模式串本身作为关联值)。
/// </summary>
public static class DualBufferAhoCorasick
{
    /// <summary>从模式串集合创建双缓冲自动机(string 关联值)。</summary>
    public static DualBufferAhoCorasick<string> Create(
        IEnumerable<string> initialPatterns, bool ignoreCase = true)
    {
        var ac = AhoCorasick.Create(initialPatterns, ignoreCase);
        return new DualBufferAhoCorasick<string>(ac, ignoreCase);
    }

    /// <summary>从模式串集合创建双缓冲自动机(bool 关联值)。</summary>
    public static DualBufferAhoCorasick<bool> CreateBool(
        IEnumerable<string> initialPatterns, bool ignoreCase = true)
    {
        var ac = AhoCorasick.CreateBool(initialPatterns, ignoreCase);
        return new DualBufferAhoCorasick<bool>(ac, ignoreCase);
    }
}
