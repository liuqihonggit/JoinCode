namespace JoinCode.Abstractions.Attributes;

/// <summary>
/// 标记一个类为情景模式（scenario）入口 — 源码生成器据此扫描收集，生成 ScenarioRegistry 静态注册表。
/// 情景模式是工具说明书：一组工具 + 建议编排流程 + 提示，供 AI 通过 <c>jcc -h scenario</c> 渐进式发现。
/// </summary>
/// <remarks>
/// 对齐 AGENTS.md"字典配置"规范：AOT 元编程 + 特性 + 源码生成器，禁止手写注册表。
/// 新增情景模式只需加此特性，无需改 ScenarioRegistry（由生成器自动收集）。
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ScenarioAttribute : Attribute {
    /// <summary>情景模式名称 — 唯一标识，用于 <c>jcc -h scenario &lt;name&gt;</c> 查询。</summary>
    public string Name { get; }

    /// <summary>情景模式描述 — 一句话说明该场景的用途与方法。</summary>
    public string Description { get; }

    /// <summary>
    /// 该情景涉及的工具集 — 按建议调用顺序排列。
    /// 源码生成器据此生成 ScenarioInfo.Tools 数组。
    /// </summary>
    public string[] Tools { get; set; } = [];

    /// <summary>建议编排流程 — 如 <c>look → zoom → detect → click</c>。</summary>
    public string SuggestedFlow { get; set; } = "";

    /// <summary>使用提示 — 注意事项、常见错误、回退策略等。</summary>
    public string Tips { get; set; } = "";

    /// <summary>构造 ScenarioAttribute 实例。</summary>
    /// <param name="name">情景模式名称。</param>
    /// <param name="description">情景模式描述。</param>
    public ScenarioAttribute(string name, string description) {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Description = description ?? throw new ArgumentNullException(nameof(description));
    }
}
