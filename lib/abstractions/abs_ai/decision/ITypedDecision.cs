
namespace JoinCode.Abstractions.Decision;

/// <summary>
/// 类型化决策抽象 — Jev 等 System One 模型返回的单个决策结果
/// 与 LLM 文本生成的 ApiMessage 解耦:决策承载概率/分类/评分,非自由文本
/// 实现方:JevDecision(Jev)、未来分类器/评分器/路由器
/// </summary>
public interface ITypedDecision {
    /// <summary>问题标识(对应请求中 questions 字典的 key)</summary>
    string QuestionName { get; }

    /// <summary>决策原语类型(Noul 是非概率 / Choice 分类 / Score 评分)</summary>
    TypedDecisionKind Kind { get; }

    /// <summary>置信度 0.0-1.0,模型对该决策的自信程度</summary>
    double Confidence { get; }

    /// <summary>原始值 — Noul: float 概率 / Choice: string 选项 / Score: float 分数。JsonElement 承载,AOT 安全</summary>
    JsonElement RawValue { get; }
}
