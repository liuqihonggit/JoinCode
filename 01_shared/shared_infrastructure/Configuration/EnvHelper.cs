namespace Infrastructure.Configuration;

/// <summary>
/// 环境变量统一读取入口 — 对齐 TS 版 envUtils.ts
/// 所有环境变量读取必须通过此类，禁止直接调用 Environment.GetEnvironmentVariable
/// </summary>
public static class EnvHelper
{
    /// <summary>
    /// 获取 JCC 专属环境变量
    /// </summary>
    public static string? Get(JccEnvVar var) => Environment.GetEnvironmentVariable(var.ToValue());

    /// <summary>
    /// 获取 Provider 专属环境变量
    /// </summary>
    public static string? Get(ProviderEnvVar var) => Environment.GetEnvironmentVariable(var.ToValue());

    /// <summary>
    /// 获取环境变量，带默认值
    /// </summary>
    public static string GetOr(JccEnvVar var, string defaultValue) => Get(var) ?? defaultValue;

    /// <summary>
    /// 获取环境变量，带默认值
    /// </summary>
    public static string GetOr(ProviderEnvVar var, string defaultValue) => Get(var) ?? defaultValue;

    /// <summary>
    /// 判断环境变量是否为真值（"1", "true", "yes"）
    /// </summary>
    public static bool IsTruthy(JccEnvVar var) => IsTruthy(Get(var));

    /// <summary>
    /// 判断环境变量是否为真值（"1", "true", "yes"）
    /// </summary>
    public static bool IsTruthy(ProviderEnvVar var) => IsTruthy(Get(var));

    /// <summary>
    /// 判断字符串是否为真值
    /// </summary>
    public static bool IsTruthy(string? value) => value is "1" or "true" or "yes";

    /// <summary>
    /// 设置 JCC 专属环境变量
    /// </summary>
    public static void Set(JccEnvVar var, string? value) => Environment.SetEnvironmentVariable(var.ToValue(), value);

    /// <summary>
    /// 设置 Provider 专属环境变量
    /// </summary>
    public static void Set(ProviderEnvVar var, string? value) => Environment.SetEnvironmentVariable(var.ToValue(), value);
}
