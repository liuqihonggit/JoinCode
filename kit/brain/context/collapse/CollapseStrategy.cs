
namespace Core.Context.Collapse;

/// <summary>
/// 上下文折叠策略
/// </summary>
public enum CollapseStrategy
{
    /// <summary>
    /// 激进策略：优先折叠更多段，压缩比更高
    /// </summary>
    [EnumValue("aggressive")] Aggressive,
    /// <summary>
    /// 平衡策略：在压缩比与信息保留之间取得平衡
    /// </summary>
    [EnumValue("balanced")] Balanced,
    /// <summary>
    /// 保守策略：仅折叠高优先级段，最大程度保留原始信息
    /// </summary>
    [EnumValue("conservative")] Conservative
}
