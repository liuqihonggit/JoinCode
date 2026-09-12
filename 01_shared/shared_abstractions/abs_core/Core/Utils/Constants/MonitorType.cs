namespace JoinCode.Abstractions.Utils;

/// <summary>
/// MCP 监控类型枚举
/// [EnumValue] 特性由 EnumMetadataGenerator 自动生成 MonitorTypeConstants + MonitorTypeExtensions
/// </summary>
public enum MonitorType
{
    /// <summary>状态概览</summary>
    [EnumValue("status")] Status = 0,

    /// <summary>工具列表</summary>
    [EnumValue("tools")] Tools = 1,

    /// <summary>客户端列表</summary>
    [EnumValue("clients")] Clients = 2,

    /// <summary>健康检查</summary>
    [EnumValue("health")] Health = 3
}
