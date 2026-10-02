namespace JoinCode.Abstractions.CodeIndex;

/// <summary>
/// 代码检索模式 — 区分 6 种检索策略，用于 code_query 统一工具。二次方赋值支持位运算组合。
/// </summary>
public enum SearchMode {
    /// <summary>混合召回（默认）— 符号+向量+文档并行，去重合并+图谱展开。</summary>
    [EnumValue("hybrid")]
    Hybrid = 1,

    /// <summary>单纯符号检索 — Token AND-OR 匹配 AST 符号。</summary>
    [EnumValue("symbol")]
    Symbol = 2,

    /// <summary>单纯向量检索 — ONNX 语义嵌入 + 余弦相似度。</summary>
    [EnumValue("vector")]
    Vector = 4,

    /// <summary>代码图谱关系 — 调用方/被调用方/引用/依赖/路径/子图。</summary>
    [EnumValue("graph")]
    Graph = 8,

    /// <summary>文档检索 — .md 文件语义搜索。</summary>
    [EnumValue("document")]
    Document = 16,

    /// <summary>项目关系 — 依赖/被依赖/受影响/NuGet。</summary>
    [EnumValue("project")]
    Project = 32
}
