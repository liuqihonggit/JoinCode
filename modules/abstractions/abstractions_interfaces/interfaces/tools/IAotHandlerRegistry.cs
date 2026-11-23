namespace JoinCode.Abstractions.Tools;

/// <summary>
/// AOT handler 注册表接口 — 编译期源码生成器产出的 handler 路由表抽象
/// <para>AOT 模式下 DynamicPluginRegistry.Invoke 从此接口获取 handler 表</para>
/// <para>节点 ID 对齐 ToolCategory 的 [EnumValue] 字符串（如 "shell"、"file"、"web"）</para>
/// </summary>
public interface IAotHandlerRegistry {
    /// <summary>
    /// 获取指定节点的 handler 路由表
    /// </summary>
    /// <param name="nodeId">节点 ID（对齐 ToolCategory.[EnumValue]）</param>
    /// <returns>方法名 → handler 委托的路由表；节点不存在返回 null</returns>
    FrozenDictionary<string, Func<object?[], object?>>? GetHandlers(string nodeId);
}
