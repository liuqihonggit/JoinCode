
namespace Core.DependencyInjection;

/// <summary>
/// 节点工厂汇聚点 — 把生成器产出的静态节点工厂目录暴露给装配层。
/// <para>
/// TASK100：<c>Pipelines</c> 是管道装配层，但其编译闭包并不覆盖全部实现域
/// （实证：<c>JoinCode.Guard.Sandbox</c> 不可见，<c>global::</c> 限定名亦报 CS0234）。
/// 而 <c>Composition</c> 拥有最宽闭包（它的生成注册里就引用了各域实现类型），
/// 因此由本程序集持有全量节点工厂目录，<c>Pipelines</c> 只做转发。
/// </para>
/// <para>
/// 目录由源生成器 <c>NodeFactoryCatalogGenerator</c> 静态生成，无反射、无运行期扫描；
/// <c>--dump-nodes</c> 取键列表时不需要建真实宿主容器。
/// </para>
/// </summary>
public static class NodeFactoryContribution {
    /// <summary>
    /// 枚举本程序集编译闭包内所有 <c>[NodeFactory]</c> 标注产生的节点工厂。
    /// </summary>
    /// <returns>键为 <c>consumer:nodeId</c> 的工厂序列。</returns>
    public static IEnumerable<KeyValuePair<string, Func<IServiceProvider, object>>> Create() {
        return NodeFactoryCatalog.Create();
    }
}
