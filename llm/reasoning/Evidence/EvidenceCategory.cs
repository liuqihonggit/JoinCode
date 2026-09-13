namespace JoinCode.Reasoning.Evidence;

/// <summary>
/// 证据类型分类
/// </summary>
public enum EvidenceCategory
{
    /// <summary>合同类证据</summary>
    [EnumValue("contractual")] Contractual,
    /// <summary>财务类证据</summary>
    [EnumValue("financial")] Financial,
    /// <summary>证词类证据（人证）</summary>
    [EnumValue("testimonial")] Testimonial,
    /// <summary>文档类证据（书证）</summary>
    [EnumValue("documentary")] Documentary,
    /// <summary>实物类证据（物证）</summary>
    [EnumValue("physical")] Physical,
    /// <summary>专家意见类证据</summary>
    [EnumValue("expert_opinion")] ExpertOpinion,
    /// <summary>旁证（间接证据）</summary>
    [EnumValue("circumstantial")] Circumstantial,
    /// <summary>数字类证据（电子数据）</summary>
    [EnumValue("digital")] Digital,
    /// <summary>司法认知（法院已知事实，无需举证）</summary>
    [EnumValue("judicial_notice")] JudicialNotice,
}
