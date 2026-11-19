namespace JoinCode.Abstractions.Attributes;

/// <summary>
/// 节点工厂标注 — 让「实现所在程序集」自己贡献节点工厂，装配层不再认识具体类型。
/// <para>
/// TASK100：此前 176 个节点的工厂全部集中在 <c>DataDrivenPipelineComposition.BuildNodeFactories()</c>，
/// 而该方法位于 <c>Pipelines</c> 程序集，其编译闭包并不覆盖所有域
/// （实证：<c>JoinCode.Guard.Sandbox</c> 不在其中，连 <c>global::</c> 限定名都报 CS0234），
/// 因此「实现不在 Pipelines 闭包内」的域天生当不了节点。
/// </para>
/// <para>
/// 标注后由源生成器按编译单元聚合出**静态**工厂清单（非反射、非运行期扫描，AOT 友好），
/// 装配层只做汇总。清单由最宽闭包的程序集（<c>Composition</c>）持有并对外暴露。
/// </para>
/// <para>
/// 约束：被标注的类必须能被 DI 解析（自身或接口已标 <c>[Register]</c>），
/// 否则运行期取工厂实例时会报「未注册」。
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class NodeFactoryAttribute : Attribute {
    /// <summary>消费点 ID（如 sandbox-providers）。</summary>
    public string Consumer { get; }

    /// <summary>节点 ID（kebab-case，如 soft-sandbox-provider）。</summary>
    public string NodeId { get; }

    /// <summary>构造节点工厂标注。</summary>
    /// <param name="consumer">消费点 ID。</param>
    /// <param name="nodeId">节点 ID（kebab-case）。</param>
    public NodeFactoryAttribute(string consumer, string nodeId) {
        Consumer = consumer ?? throw new ArgumentNullException(nameof(consumer));
        NodeId = nodeId ?? throw new ArgumentNullException(nameof(nodeId));
    }

    /// <summary>拼装成装配层使用的完整键（consumer:nodeId）。</summary>
    public string Key => $"{Consumer}:{NodeId}";
}
