
namespace JoinCode.Abstractions.Decision;

/// <summary>
/// 类型化决策问题定义 — 对应 Jev 请求中 questions 字典的 value
/// 与具体模型解耦:Kind + Instructions + Options 通用描述任意 System One 问题
/// </summary>
public sealed class TypedDecisionQuestion {
    /// <summary>决策原语类型(Noul 是非概率 / Choice 分类 / Score 评分)</summary>
    public TypedDecisionKind Kind { get; init; }

    /// <summary>问题指令 — 告诉模型如何判断/分类/评分</summary>
    public string Instructions { get; init; } = string.Empty;

    /// <summary>选项列表(仅 Choice 类型需要,Noul/Score 为空集合)</summary>
    public IReadOnlyList<string> Options { get; init; } = [];
}
