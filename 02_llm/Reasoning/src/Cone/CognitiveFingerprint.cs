namespace JoinCode.Reasoning.Cone;

/// <summary>
/// 认知指纹 — 描述一个片段是如何被"感知"的
/// </summary>
public sealed class CognitiveFingerprint
{
    /// <summary>
    /// 入口刺激，如"证据X在物证袋中的位置"
    /// </summary>
    public string EntryStimulus { get; init; } = string.Empty;

    /// <summary>
    /// 加工路径，如"视觉->空间关联->案例匹配"
    /// </summary>
    public string ProcessingPath { get; init; } = string.Empty;

    /// <summary>
    /// 输出结论
    /// </summary>
    public string OutputConclusion { get; init; } = string.Empty;

    /// <summary>
    /// 置信度 [0, 1]
    /// </summary>
    public double Confidence { get; init; } = 0.5;

    /// <summary>
    /// 未决怀疑列表
    /// </summary>
    public List<string> OpenQuestions { get; init; } = [];
}
