namespace JoinCode.Abstractions.Localization;

/// <summary>
/// 静态本地化入口 - 零依赖，AOT安全，InvariantGlobalization安全
/// 默认语言为中文，设置 JCC_LANGUAGE=en 切换为英文。
/// 生产环境：Program.cs 调用 LocalizerInitializer.Initialize() 初始化完整字典
/// 测试环境：L.T() 首次调用时通过 LazyInitializer 自动初始化
/// 线程安全：Initialize 和 EnsureInitialized 通过 lock 保护，T() 读取 FrozenDictionary 是线程安全的
/// </summary>
public static class L
{
    private static readonly object s_lock = new();
    private static IReadOnlyDictionary<string, string> _entries =
        FrozenDictionary<string, string>.Empty;

    /// <summary>
    /// 当前语言代码（默认 "zh"）
    /// </summary>
    public static string CurrentLanguage { get; private set; } = "zh";

    private static volatile bool _initialized;

    /// <summary>
    /// 初始化本地化系统。可多次调用（后者覆盖前者）。线程安全。
    /// </summary>
    public static void Initialize(string language, IReadOnlyDictionary<string, string> entries)
    {
        lock (s_lock)
        {
            CurrentLanguage = language;
            _entries = entries;
            _initialized = true;
        }
    }

    /// <summary>
    /// 懒初始化委托，由 Infrastructure 项目通过 ModuleInitializer 设置
    /// </summary>
    public static Action? LazyInitializer { get; set; }

    /// <summary>
    /// 获取本地化字符串。未找到 key 时返回 key 本身。
    /// 首次调用时自动初始化本地化系统（默认中文）。线程安全。
    /// </summary>
    public static string T(string key)
    {
        if (!_initialized)
            EnsureInitialized();
        // _entries 是 FrozenDictionary，读取是线程安全的
        return _entries.TryGetValue(key, out var value) ? value : key;
    }

    private static void EnsureInitialized()
    {
        lock (s_lock)
        {
            if (!_initialized)
            {
                // 通过 ModuleInitializer 注册的委托初始化
                LazyInitializer?.Invoke();
            }
        }
    }

    /// <summary>
    /// 获取本地化字符串并格式化（1个参数）
    /// </summary>
    public static string T(string key, object? arg0)
        => string.Format(CultureInfo.InvariantCulture, T(key), arg0);

    /// <summary>
    /// 获取本地化字符串并格式化（2个参数）
    /// </summary>
    public static string T(string key, object? arg0, object? arg1)
        => string.Format(CultureInfo.InvariantCulture, T(key), arg0, arg1);

    /// <summary>
    /// 获取本地化字符串并格式化（3个参数）
    /// </summary>
    public static string T(string key, object? arg0, object? arg1, object? arg2)
        => string.Format(CultureInfo.InvariantCulture, T(key), arg0, arg1, arg2);

    /// <summary>
    /// 获取本地化字符串并格式化（任意数量参数）
    /// </summary>
    public static string T(string key, params object?[] args)
        => string.Format(CultureInfo.InvariantCulture, T(key), args);
}
