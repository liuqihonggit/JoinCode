namespace JoinCode.Abstractions.Interfaces.Cache;

/// <summary>
/// 会话环境变量缓存接口 — 对齐 TS: clearSessionEnvVars
/// </summary>
public interface ISessionEnvVars
{
    /// <summary>
    /// 清除所有缓存
    /// </summary>
    void Clear();
}
