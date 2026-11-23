namespace JoinCode.Reasoning.Weight.Calculator;

/// <summary>
/// 证据来源分类 — 用于来源可信度权重查找
/// <para>字符串值通过 [EnumValue] 统一管理,消费方通过 FromValue() 解析</para>
/// </summary>
public enum EvidenceSourceCategory {
    /// <summary>政府机构 — 可信度 0.95</summary>
    [EnumValue("政府机构")] GovernmentAgency,
    /// <summary>法院判决 — 可信度 0.90</summary>
    [EnumValue("法院判决")] CourtJudgment,
    /// <summary>银行系统 — 可信度 0.88</summary>
    [EnumValue("银行系统")] BankingSystem,
    /// <summary>公证文件 — 可信度 0.85</summary>
    [EnumValue("公证文件")] NotarizedDocument,
    /// <summary>媒体报道 — 可信度 0.60</summary>
    [EnumValue("媒体报道")] MediaReport,
    /// <summary>个人陈述 — 可信度 0.40</summary>
    [EnumValue("个人陈述")] PersonalStatement,
    /// <summary>匿名来源 — 可信度 0.15</summary>
    [EnumValue("匿名来源")] AnonymousSource,
}
