namespace Api;

[Register(typeof(IToolGroupFactory))]
public sealed class ToolGroupFactory : IToolGroupFactory
{
    public IToolGroup CreateFromObject(object instance, string pluginName)
    {
        throw new NotSupportedException(
            "CreateFromObject 不再支持反射扫描。请使用 McpToolBridge.CreatePluginAsync 或手动构建 ToolGroup。");
    }
}
