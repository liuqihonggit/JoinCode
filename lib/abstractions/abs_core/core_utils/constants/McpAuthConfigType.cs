namespace JoinCode.Abstractions.Utils;

/// <summary>
/// MCP认证配置类型枚举 — 用于McpAuthToolHandlers持久化标识
/// [EnumValue] 特性由 EnumMetadataGenerator 自动生成 McpAuthConfigTypeConstants + McpAuthConfigTypeExtensions
/// 注意: 与 McpClient.McpAuthType（客户端选项枚举）不同，本枚举用于Handler层的认证配置持久化
/// </summary>
public enum McpAuthConfigType
{
    /// <summary>API密钥认证</summary>
    [EnumValue("apikey")] ApiKey = 0,

    /// <summary>Bearer令牌认证</summary>
    [EnumValue("bearer")] Bearer = 1,

    /// <summary>Basic认证</summary>
    [EnumValue("basic")] Basic = 2,

    /// <summary>OAuth2认证</summary>
    [EnumValue("oauth2")] OAuth2 = 3
}
