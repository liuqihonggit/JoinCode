namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 插件依赖声明 — 插件可选实现此接口声明依赖的其他插件
/// <para>对齐 Cordis 声明式依赖: 插件不猜测环境,显式声明依赖,框架据此做连带卸载</para>
/// <para>卸载插件 A 时,框架先连带卸载所有声明依赖 A 的插件 B,最后卸载 A 本身(Theorem 63)</para>
/// </summary>
public interface IPluginDependencies
{
    /// <summary>此插件依赖的插件名列表</summary>
    IReadOnlyList<string> Dependencies { get; }
}
