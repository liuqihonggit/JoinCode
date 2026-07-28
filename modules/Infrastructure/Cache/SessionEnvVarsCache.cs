namespace Infrastructure.Cache;

/// <summary>
/// 会话环境变量缓存实现 — 对齐 TS: clearSessionEnvVars
/// </summary>
[Register(typeof(JoinCode.Abstractions.Interfaces.Cache.ISessionEnvVars))]
public sealed partial class SessionEnvVarsCache : JoinCode.Abstractions.Interfaces.Cache.ISessionEnvVars
{
    private readonly Dictionary<string, string> _vars = new(StringComparer.OrdinalIgnoreCase);

    public void Clear() => _vars.Clear();

    public string? Get(string key) => _vars.GetValueOrDefault(key);

    public void Set(string key, string value) => _vars[key] = value;
}
