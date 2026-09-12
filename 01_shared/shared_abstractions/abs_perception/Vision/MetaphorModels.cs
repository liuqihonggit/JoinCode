namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 图像描述结果 — M2 顶层粗粒度识别（image_describe 工具返回）
/// </summary>
/// <param name="Summary">图片整体摘要描述</param>
/// <param name="Labels">识别到的标签列表，每个标签含可下钻属性建议</param>
public sealed record ImageDescriptionResult(
    [property: JsonPropertyName("summary")] string Summary,
    [property: JsonPropertyName("labels")] List<ImageLabel> Labels);

/// <summary>
/// 图像标签 — M2 隐喻拓扑节点，含下钻属性建议供 LLM 决策
/// </summary>
/// <param name="Label">标签名（如"冰箱"、"桌子"）</param>
/// <param name="Description">简短描述</param>
/// <param name="SuggestedAttributes">可下钻属性列表（如["品牌","颜色","状态"]）</param>
public sealed record ImageLabel(
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("suggested_attributes")] List<string> SuggestedAttributes);

/// <summary>
/// 图像下钻结果 — M2 按标签深挖细粒度属性（image_drill_down 工具返回）
/// </summary>
/// <param name="Label">被下钻的标签名</param>
/// <param name="Attributes">细粒度属性列表</param>
/// <param name="SuggestedNext">建议下一步下钻的目标</param>
/// <param name="CurrentDepth">当前下钻深度</param>
/// <param name="HasMore">是否还有更多可探索的属性</param>
public sealed record ImageDrillDownResult(
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("attributes")] List<ImageAttribute> Attributes,
    [property: JsonPropertyName("suggested_next")] List<string> SuggestedNext,
    [property: JsonPropertyName("current_depth")] int CurrentDepth,
    [property: JsonPropertyName("has_more")] bool HasMore);

/// <summary>
/// 图像属性 — M2 下钻获取的单个细粒度属性
/// </summary>
/// <param name="Name">属性名（如"品牌"）</param>
/// <param name="Value">属性值（如"海尔"）</param>
/// <param name="Confidence">置信度 0..1</param>
public sealed record ImageAttribute(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("value")] string Value,
    [property: JsonPropertyName("confidence")] double Confidence);
