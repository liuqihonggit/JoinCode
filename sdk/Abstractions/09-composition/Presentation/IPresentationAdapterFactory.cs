namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 表示层适配器工厂 — 根据运行环境创建对应的 IPresentationAdapter
/// </summary>
public interface IPresentationAdapterFactory
{
    /// <summary>
    /// 创建指定模式的表示层适配器
    /// </summary>
    IPresentationAdapter Create(PresentationMode mode);

    /// <summary>
    /// 自动检测当前环境并创建适配器
    /// </summary>
    IPresentationAdapter CreateForCurrentEnvironment();
}
