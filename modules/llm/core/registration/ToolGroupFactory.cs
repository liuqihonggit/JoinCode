namespace Api;

[Register(typeof(IToolGroupFactory), ServiceLifetime.Singleton)]
public sealed partial class ToolGroupFactory : ServiceEntity, IToolGroupFactory {
    /// <summary>
    /// 从对象创建工具组（已弃用反射扫描）。
    /// </summary>
    /// <param name="instance">工具实例。</param>
    /// <param name="pluginName">插件名称。</param>
    public IToolGroup CreateFromObject(object instance, string pluginName) {
        throw new NotSupportedException(
            "CreateFromObject 不再支持反射扫描。请使用 McpToolBridge.CreatePluginAsync 或手动构建 ToolGroup。");
    }
}