namespace JoinCode.Abstractions.Composition.Profile;

/// <summary>
/// 消费点契约 — 节点服务于哪种装配形态，决定「如何组合」。
/// <para>
/// TASK100：此前节点形态只由 <c>BuildNodeFactories()</c> 的字符串前缀隐式表达，
/// 导致只有中间件能当节点。契约显式化后，注册表可按契约校验消费点与节点是否匹配，
/// 也为 provider/role/capability 等非中间件形态留出同一套装配通道。
/// </para>
/// <para>
/// 契约不是类层次：<see cref="INode"/> 仍只有 NodeId/Metadata/State 三属性，
/// 差异全部体现在消费点的组合语义上（沿用 TASK097 G3「数据驱动、不引入 NodeBase」裁决）。
/// </para>
/// </summary>
public static class NodeContract {
    /// <summary>顺序叠加（priority 升序）—— 16 条 MiddlewarePipeline。</summary>
    public const string Middleware = "middleware";

    /// <summary>顺序叠加（priority 降序、首结论胜）—— 命令守卫链。</summary>
    public const string Guard = "guard";

    /// <summary>互斥选择（按协议/能力）—— 模型适配器。</summary>
    public const string Provider = "provider";

    /// <summary>互斥选择（按 role + variant）—— Agent 角色档案。</summary>
    public const string Role = "role";

    /// <summary>存在即提供 —— 业务能力（todo/cron/memory/dream）。</summary>
    public const string Capability = "capability";

    /// <summary>分类聚合 —— 命令表面（斜杠命令类别 / CLI 子命令）。</summary>
    public const string CommandGroup = "command-group";

    /// <summary>全部合法契约值（解析校验与错误提示的唯一清单）。</summary>
    public static IReadOnlyList<string> All { get; } =
        [Middleware, Guard, Provider, Role, Capability, CommandGroup];

    /// <summary>契约值是否合法（大小写不敏感）。</summary>
    /// <param name="contract">待校验的契约值。</param>
    /// <returns>合法返回 <see langword="true"/>。</returns>
    public static bool IsValid(string contract) =>
        All.Contains(contract, StringComparer.OrdinalIgnoreCase);

    /// <summary>规范化契约值（小写）；未知值原样返回，交由调用方报错。</summary>
    /// <param name="contract">原始契约值。</param>
    /// <returns>小写契约值。</returns>
    public static string Normalize(string contract) => contract.ToLowerInvariant();
}
