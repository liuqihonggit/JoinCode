namespace Infrastructure.Cache;

/// <summary>
/// 会话环境变量缓存实现 — 对齐 TS: clearSessionEnvVars
/// </summary>
[Register(typeof(JoinCode.Abstractions.Interfaces.Cache.ISessionEnvVars), ServiceLifetime.Singleton)]
public sealed partial class SessionEnvVarsCache : ServiceEntity, JoinCode.Abstractions.Interfaces.Cache.ISessionEnvVars {
    private readonly Dictionary<string, string> _vars = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>清空所有缓存的环境变量</summary>
    public void Clear() => _vars.Clear();

    /// <summary>
    /// 获取指定键的环境变量值
    /// </summary>
    /// <param name="key">环境变量键名（不区分大小写）</param>
    /// <returns>命中时返回值，未命中时返回 null</returns>
    public string? Get(string key) => _vars.GetValueOrDefault(key);

    /// <summary>
    /// 设置指定键的环境变量值，已存在则覆盖
    /// </summary>
    /// <param name="key">环境变量键名（不区分大小写）</param>
    /// <param name="value">环境变量值</param>
    public void Set(string key, string value) => _vars[key] = value;
}