namespace McpProtocol.Contracts;

/// <summary>
/// MCP 协议版本常量 — 对齐官方规范版本号
/// <para>单数据源:从 kit/mcp/constants/JsonRpcConstants.cs 下沉到 abstractions(底层),消除 abs_ai/test/mock 无法委托的架构约束</para>
/// <para>> ADR: TASK031 阶段B4 — 常量委托统一</para>
/// </summary>
public static class McpProtocolVersion {
    /// <summary>2024-11-05 规范版本(已归档)</summary>
    public const string V2024_11_05 = "2024-11-05";
    /// <summary>2025-03-26 规范版本</summary>
    public const string V2025_03_26 = "2025-03-26";
    /// <summary>2025-06-18 规范版本</summary>
    public const string V2025_06_18 = "2025-06-18";
    /// <summary>2025-11-25 规范版本(Streamable HTTP)</summary>
    public const string V2025_11_25 = "2025-11-25";

    /// <summary>当前生效的 MCP 协议版本</summary>
    public const string Current = V2025_11_25;

    /// <summary>服务端支持的协议版本集合(按优先级降序排列)</summary>
    public static readonly FrozenSet<string> Supported = FrozenSet.Create(
        StringComparer.Ordinal,
        V2025_11_25, V2025_06_18, V2025_03_26);
}
