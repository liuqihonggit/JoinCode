namespace JoinCode.Abstractions.Tools;

/// <summary>
/// AOT handler 注册表构建器接口 — 源码生成器通过此接口注册 handler
/// <para>源码生成器生成 AotHandlerRegistrations.Register(builder) 方法</para>
/// <para>builder 收集所有 (nodeId, method, handler) 注册项后构造路由表</para>
/// </summary>
public interface IAotHandlerRegistryBuilder {
    /// <summary>
    /// 注册一个 handler
    /// </summary>
    /// <param name="nodeId">节点 ID（对齐 ToolCategory.[EnumValue]）</param>
    /// <param name="method">方法名（对齐 [McpTool] 工具名）</param>
    /// <param name="handler">handler 委托 — 接受 IServiceProvider 和参数数组，返回结果</param>
    void AddHandler(string nodeId, string method, Func<IServiceProvider, object?[], object?> handler);
}
