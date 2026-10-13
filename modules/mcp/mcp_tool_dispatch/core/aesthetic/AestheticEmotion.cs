namespace McpToolRegistry;

/// <summary>
/// 情绪向量 — 工具结果触发的情绪类型，与代码品味强关联
/// </summary>
public enum AestheticEmotion {
    /// <summary>无情绪 — 结果正常，无需反馈</summary>
    None,

    /// <summary>难过 — 字段过多、职责混乱、上帝对象</summary>
    Sadness,

    /// <summary>警惕 — bug/fixme/hack/workaround 标记，需主动调查根因</summary>
    Alert,

    /// <summary>疑惑 — 死代码、断裂的函数关系图谱、无法连接的调用链</summary>
    Confusion,

    /// <summary>开心 — 环环相扣的优美设计、强迫症级别的整洁代码</summary>
    Joy
}
