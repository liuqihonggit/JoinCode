namespace JoinCode.Abstractions.Entity;

/// <summary>
/// 运行时不变量违规异常 — 对齐 DSH InvariantError
/// <para>稳定 code:'INVARIANT'（机器可读）+ 归属 packageName</para>
/// <para>不给服务本身加任何产品依赖</para>
/// </summary>
public sealed class InvariantError : Exception
{
    /// <summary>稳定错误码（机器可读）</summary>
    public const string Code = "INVARIANT";

    /// <summary>违规的包名（精确 npm/nuget 包名）</summary>
    public string PackageName { get; }

    /// <param name="packageName">违规包名</param>
    /// <param name="message">违规描述</param>
    public InvariantError(string packageName, string message)
        : base($"invariant violated by \"{packageName}\": {message}")
    {
        PackageName = packageName;
    }
}

/// <summary>
/// 不变量安装器 — 接收 fail 注入，在违规时调用 fail 抛 InvariantError
/// <para>对齐 DSH InvariantInstaller：(fail) => { ... if (违规) fail("描述") }</para>
/// </summary>
public delegate void InvariantInstaller(Action<string> fail);

/// <summary>
/// 不变量注册表配置 — 对齐 DSH ctx.invariants Config
/// <para>Enabled：服务级总开关，默认 true</para>
/// <para>PackageAllowlist：正则源数组，空则全部放行</para>
/// <para>PackageBlocklist：正则源数组，blocklist 优先于 allowlist</para>
/// </summary>
public sealed class InvariantRegistryOptions
{
    /// <summary>服务级总开关，默认 true</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>正则源数组（区分大小写），空则全部放行</summary>
    public string[]? PackageAllowlist { get; set; }

    /// <summary>正则源数组（区分大小写），blocklist 优先于 allowlist</summary>
    public string[]? PackageBlocklist { get; set; }
}

/// <summary>
/// 运行时不变量注册表 — 对齐 DSH ctx.invariants + ./invariant 配套入口
/// <para>每个插件包用 RegisterInvariants 入口注册对自身包名契约的运行时检查</para>
/// <para>违规抛带稳定 code:'INVARIANT' 和 packageName 的 InvariantError</para>
/// <para>正则过滤：enabled ∧ (allowlist 空 ∨ 匹配) ∧ 无 blocklist 匹配</para>
/// <para>启动 join：注册成功前不返回；失败原子移除注册，绝不留半注册</para>
/// </summary>
public sealed class InvariantRegistry
{
    private readonly ConcurrentDictionary<string, bool> _registrations = new();
    private readonly InvariantRegistryOptions _options;
    private readonly Regex[] _allowlist;
    private readonly Regex[] _blocklist;

    /// <param name="options">配置，null 用默认</param>
    public InvariantRegistry(InvariantRegistryOptions? options = null)
    {
        _options = options ?? new InvariantRegistryOptions();
        _allowlist = CompilePatterns(_options.PackageAllowlist);
        _blocklist = CompilePatterns(_options.PackageBlocklist);
    }

    /// <summary>
    /// 注册包的不变量检查 — 返回 disposer
    /// <para>保留活动注册：即使过滤器让 installer 非活动，包名也被保留（占位）</para>
    /// <para>fail 注入：installer 调用 fail 抛 InvariantError（绑定 packageName）</para>
    /// <para>启动 join：installer 失败原子移除注册</para>
    /// </summary>
    public IDisposable Register(string packageName, InvariantInstaller installer)
    {
        ArgumentNullException.ThrowIfNull(packageName);
        ArgumentNullException.ThrowIfNull(installer);

        _registrations[packageName] = true;

        if (!IsSelected(packageName))
        {
            return new RegistrationDisposer(() => _registrations.TryRemove(packageName, out _));
        }

        void Fail(string message) => throw new InvariantError(packageName, message);

        try
        {
            installer(Fail);
        }
        catch (InvariantError)
        {
            _registrations.TryRemove(packageName, out _);
            throw;
        }

        return new RegistrationDisposer(() => _registrations.TryRemove(packageName, out _));
    }

    /// <summary>
    /// 包是否被选中
    /// <para>三个条件缺一不可：启用 ∧ (allowlist 空 ∨ 至少一个匹配) ∧ 无 blocklist 匹配</para>
    /// </summary>
    public bool IsSelected(string packageName)
    {
        if (!_options.Enabled) return false;
        if (_allowlist.Length > 0 && !AnyMatch(_allowlist, packageName)) return false;
        return !AnyMatch(_blocklist, packageName);
    }

    /// <summary>已注册的包名集合</summary>
    public ICollection<string> RegisteredPackages => _registrations.Keys;

    private static bool AnyMatch(Regex[] regexes, string input)
    {
        for (int i = 0; i < regexes.Length; i++)
        {
            if (regexes[i].IsMatch(input)) return true;
        }
        return false;
    }

    private static Regex[] CompilePatterns(string[]? patterns)
    {
        if (patterns is null || patterns.Length == 0) return Array.Empty<Regex>();
        var regexes = new Regex[patterns.Length];
        for (int i = 0; i < patterns.Length; i++)
        {
            regexes[i] = new Regex(patterns[i], RegexOptions.Compiled | RegexOptions.CultureInvariant);
        }
        return regexes;
    }

    private sealed class RegistrationDisposer(Action unsubscribe) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            unsubscribe();
        }
    }
}
