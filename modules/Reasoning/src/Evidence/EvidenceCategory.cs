namespace JoinCode.Reasoning.Evidence;

/// <summary>
/// 证据类型分类
/// </summary>
public enum EvidenceCategory
{
    [EnumValue("contractual")] Contractual,
    [EnumValue("financial")] Financial,
    [EnumValue("testimonial")] Testimonial,
    [EnumValue("documentary")] Documentary,
    [EnumValue("physical")] Physical,
    [EnumValue("expert_opinion")] ExpertOpinion,
    [EnumValue("circumstantial")] Circumstantial,
    [EnumValue("digital")] Digital,
    [EnumValue("judicial_notice")] JudicialNotice,
}
