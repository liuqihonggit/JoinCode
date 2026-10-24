namespace JoinCode.Gui.WebHost;

/// <summary>
/// 模型页凭据存储（TASK066 C8 <c>credentials/describe|set|unset</c>）：
/// 前端把密钥存到 <c>&lt;ROUTE&gt;_API_KEY</c> 引用名下，宿主把引用映射回引擎供应商，
/// 落盘委托 <see cref="ConfigLoader"/> 的共享 <c>~/.jcc/auth.json</c>（DPAPI 加密，与 TUI/CLI 同一份，
/// 两前端读写同一密钥），写后同步引擎内存里的当前供应商密钥，下一次请求立即生效。
/// </summary>
internal sealed class DshCredentialStore {
    private readonly IFileSystem _fs;
    private readonly AppDataPaths _paths;
    private readonly Func<IReadOnlyList<string>> _providers;

    /// <summary>读环境变量（默认进程环境；测试可注入，避免污染真实环境）</summary>
    internal Func<string, string?> ReadEnvironment { get; set; } = Environment.GetEnvironmentVariable;

    /// <summary>写密钥后同步引擎（参数：供应商、明文密钥或 null 表示移除；未注入时只落盘）</summary>
    internal Func<string, string?, CancellationToken, Task>? ApplyToEngine { get; set; }

    /// <summary>构造凭据存储</summary>
    /// <param name="fileSystem">文件系统抽象（auth.json 读写）</param>
    /// <param name="providers">当前可路由供应商（模型目录分组，路由表按调用时快照）</param>
    /// <param name="paths">应用数据路径（默认 <see cref="AppDataPaths.Default"/>）</param>
    internal DshCredentialStore(IFileSystem fileSystem, Func<IReadOnlyList<string>> providers, AppDataPaths? paths = null) {
        _fs = fileSystem;
        _providers = providers;
        _paths = paths ?? AppDataPaths.Default;
    }

    /// <summary>由供应商路由推导 DSH 的凭据引用名（对齐前端 deriveKeyRef：大写 + 非字母数字折成下划线）</summary>
    /// <param name="provider">供应商路由（如 deepseek / minimax-cn）</param>
    internal static string DeriveKeyRef(string provider) {
        var builder = new StringBuilder();
        foreach (var character in provider.Trim().ToUpperInvariant()) {
            if (char.IsAsciiLetterOrDigit(character)) {
                builder.Append(character);
            } else if (builder.Length > 0 && builder[^1] != '_') {
                builder.Append('_');
            }
        }
        return builder.ToString().TrimEnd('_') + "_API_KEY";
    }

    /// <summary>引用名 → 供应商路由（当前模型目录分组；同一供应商重复出现时取首个）</summary>
    internal IReadOnlyDictionary<string, string> Routes() {
        var routes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var provider in _providers().Where(provider => !string.IsNullOrWhiteSpace(provider)).Distinct(StringComparer.OrdinalIgnoreCase)) {
            routes.TryAdd(DeriveKeyRef(provider), provider);
        }
        return routes;
    }

    /// <summary>
    /// 批量查询凭据状态：环境变量优先（无法由宿主移除），其次共享 auth.json；
    /// 只有属于当前供应商路由的引用才标记为可写（前端据此决定是否显示编辑/删除）。
    /// </summary>
    /// <param name="refs">凭据引用名</param>
    /// <param name="cancellationToken">取消令牌</param>
    internal async Task<Dictionary<string, DshCredentialState>> DescribeAsync(IReadOnlyList<string> refs, CancellationToken cancellationToken) {
        var routes = Routes();
        var states = new Dictionary<string, DshCredentialState>(StringComparer.Ordinal);
        foreach (var name in refs.Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.Ordinal)) {
            var writable = routes.TryGetValue(name, out var provider);
            states[name] = await DescribeOneAsync(name, provider ?? name, writable, cancellationToken);
        }
        return states;
    }

    /// <summary>单个引用的状态（环境变量 → auth.json → 未配置）</summary>
    private async Task<DshCredentialState> DescribeOneAsync(string name, string key, bool writable, CancellationToken cancellationToken) {
        if (!string.IsNullOrEmpty(ReadEnvironment(name))) {
            return new DshCredentialState { Configured = true, Source = "env", Writable = writable };
        }
        var stored = await ConfigLoader.LoadStoredApiKeyAsync(key, _fs, cancellationToken, _paths);
        return new DshCredentialState {
            Configured = !string.IsNullOrEmpty(stored),
            Source = string.IsNullOrEmpty(stored) ? null : "stored",
            Writable = writable,
        };
    }

    /// <summary>查询单个引用的明文密钥（模型发现复用已存凭据；环境变量优先）</summary>
    /// <param name="refName">凭据引用名</param>
    /// <param name="cancellationToken">取消令牌</param>
    internal async Task<string?> FindAsync(string refName, CancellationToken cancellationToken) {
        if (ReadEnvironment(refName) is { Length: > 0 } fromEnvironment) {
            return fromEnvironment;
        }
        var key = Routes().TryGetValue(refName, out var provider) ? provider : refName;
        var stored = await ConfigLoader.LoadStoredApiKeyAsync(key, _fs, cancellationToken, _paths);
        return string.IsNullOrEmpty(stored) ? null : stored;
    }

    /// <summary>
    /// 保存密钥：只接受当前供应商路由的引用（未知引用给出可写引用清单，避免前端反复试错）。
    /// </summary>
    /// <param name="refName">凭据引用名</param>
    /// <param name="value">明文密钥</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>成功标记与失败引导（成功时文案为空）</returns>
    internal async Task<(bool Ok, string Message)> SetAsync(string refName, string value, CancellationToken cancellationToken) {
        var routes = Routes();
        if (!routes.TryGetValue(refName, out var provider)) {
            return (false, UnknownRefMessage(refName, routes));
        }
        var key = value.Trim();
        if (key.Length == 0) {
            return (false, $"凭据引用 {refName} 的密钥为空；请填写供应商控制台签发的 API Key 后重试");
        }
        await ConfigLoader.SaveApiKeyToJccAsync(provider, key, _fs, cancellationToken, paths: _paths);
        if (ApplyToEngine is { } apply) {
            await apply(provider, key, cancellationToken);
        }
        return (true, "");
    }

    /// <summary>
    /// 移除密钥：只删除共享 auth.json 中的条目；密钥来自环境变量时诚实拒绝并指出变量名
    /// （宿主无权修改进程环境）。
    /// </summary>
    /// <param name="refName">凭据引用名</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>成功标记与失败引导（成功时文案为空）</returns>
    internal async Task<(bool Ok, string Message)> UnsetAsync(string refName, CancellationToken cancellationToken) {
        var routes = Routes();
        if (!routes.TryGetValue(refName, out var provider)) {
            return (false, UnknownRefMessage(refName, routes));
        }
        if (!string.IsNullOrEmpty(ReadEnvironment(refName))) {
            return (false, $"供应商 {provider} 的密钥来自环境变量 {refName}；宿主无法移除环境变量，请删除该变量后重启，或改在模型页保存新密钥");
        }
        var removed = await ConfigLoader.RemoveApiKeyFromJccAsync(provider, _fs, cancellationToken, _paths);
        if (!removed) {
            return (false, $"供应商 {provider} 当前没有已保存的密钥（auth.json 无 {provider} 键），无需移除");
        }
        if (ApplyToEngine is { } apply) {
            await apply(provider, null, cancellationToken);
        }
        return (true, "");
    }

    /// <summary>未知引用引导：指明收到什么 + 可写引用清单 + 下一步怎么做</summary>
    private static string UnknownRefMessage(string refName, IReadOnlyDictionary<string, string> routes) =>
        routes.Count == 0
            ? $"未知凭据引用 {refName}；宿主当前没有可路由的供应商（模型目录为空），请先在设置中配置供应商再保存密钥"
            : $"未知凭据引用 {refName}；宿主只管理当前供应商的以下引用：{string.Join("、", routes.Keys)}。请按模型页展示的供应商填写";
}
