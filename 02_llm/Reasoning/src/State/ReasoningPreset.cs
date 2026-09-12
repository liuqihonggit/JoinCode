namespace JoinCode.Reasoning.State;

/// <summary>
/// 推理严格度预设 — 对应不同罪名的证明标准
/// </summary>
public enum ReasoningPreset
{
    /// <summary>
    /// 杀人罪 — 证据阈值最高，闭环锁死，排除合理怀疑
    /// </summary>
    [EnumValue("murder")] Murder,

    /// <summary>
    /// 吃熊猫罪 — 证据阈值动态，视情节浮动，中间层
    /// </summary>
    [EnumValue("panda")] Panda,

    /// <summary>
    /// 离婚官司 — 证据阈值最低，高度盖然性即可
    /// </summary>
    [EnumValue("divorce")] Divorce,
}
