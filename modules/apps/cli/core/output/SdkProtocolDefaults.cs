namespace JoinCode.Cli.Output;

/// <summary>
/// Claude Code SDK 输出协议默认值 — 外部协议字符串，不是本项目枚举语义。
/// <para>apiKeySource / outputStyle / permissionMode 的取值域由外部 SDK 协议定义
/// （outputStyle 还可被用户配置为任意样式名），因此集中为常量而不是枚举，
/// 避免与 CommandOrigin / ConfigPriority 等同值枚举产生语义耦合。</para>
/// </summary>
public static class SdkProtocolDefaults {
    /// <summary>apiKeySource 默认值 — 用户提供</summary>
    public const string ApiKeySourceUser = "user";

    /// <summary>outputStyle 默认值 — 内置默认样式</summary>
    public const string OutputStyleDefault = "default";

    /// <summary>permissionMode 默认值 — 默认权限模式</summary>
    public const string PermissionModeDefault = "default";
}