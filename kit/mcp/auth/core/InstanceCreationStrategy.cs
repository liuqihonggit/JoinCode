namespace McpClient;

/// <summary>
/// 实例创建策略 — 指定用何种方式创建对象实例
/// </summary>
public enum InstanceCreationStrategy
{
    /// <summary>
    /// 使用 Activator.CreateInstance 创建实例（调用公共构造函数）
    /// </summary>
    [EnumValue("activator")]
    Activator,

    /// <summary>
    /// 使用 System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject 创建实例（不调用构造函数）
    /// </summary>
    [EnumValue("uninitializedObject")]
    UninitializedObject,
}
