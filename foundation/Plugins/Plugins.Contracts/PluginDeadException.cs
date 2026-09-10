namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 插件死亡异常 — 使用已死亡插件的资源时抛出
/// <para>惰性检测:每次使用上层资源时调用 EnsureAlive,心跳停止则抛此异常</para>
/// </summary>
public sealed class PluginDeadException : Exception
{
    /// <summary>死亡插件的资源名</summary>
    public string ResourceName { get; }

    /// <summary>死亡插件的插件名</summary>
    public string PluginName { get; }

    /// <summary>创建插件死亡异常</summary>
    public PluginDeadException(string resourceName, string pluginName)
        : base($"资源 {resourceName} (插件 {pluginName}) 心跳已停止")
    {
        ResourceName = resourceName;
        PluginName = pluginName;
    }
}
