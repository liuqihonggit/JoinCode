namespace JoinCode.Composition.Improvement;

/// <summary>
/// 经验条目 — 对齐 Harness² Improvement library 的 EXPERIENCE.md schema。
/// <para>有效经验的必需字段为 cue / lesson / evidence（DSG048 §2.2.4 has_experience）；
/// <see cref="Patch"/> 是可复用 harness patch，让经验能真实改变 profile 而非"可配置的死数据"。</para>
/// </summary>
/// <param name="Cue">触发线索 — 什么情境下该复用这条经验（用于 Propose 按 cue 匹配）。</param>
/// <param name="Lesson">可迁移教训 — 一句话说清「该怎么做」。</param>
/// <param name="Evidence">行为证据 — 真机行为对比结果，证明经验有效（非 ground truth 打分）。</param>
/// <param name="Patch">可选的可复用 harness patch；为 null 时该经验只作为教训参考，不改变行为。</param>
public sealed record ImprovementEntry(
    string Cue,
    string Lesson,
    string Evidence,
    HarnessPatch? Patch = null);

/// <summary>
/// 可复用的 harness patch — 经验里"能真实改变行为"的那部分。
/// <para>语义等价于 NodeProfileOverride：定位到 consumer/node，覆写其一个 config 键；
/// 未提及的键从 H₀ 保留（省略即继承）。</para>
/// </summary>
/// <param name="Consumer">目标消费点 ID。</param>
/// <param name="Node">目标节点 ID。</param>
/// <param name="ConfigKey">要覆写的 config 键。</param>
/// <param name="ConfigValue">覆写后的值。</param>
public sealed record HarnessPatch(
    string Consumer,
    string Node,
    string ConfigKey,
    string ConfigValue);

/// <summary>
/// 经验库 JSONL 序列化上下文 — NativeAOT 兼容（源生成，无运行时反射）。
/// </summary>
[JsonSerializable(typeof(ImprovementEntry))]
[JsonSerializable(typeof(HarnessPatch))]
[JsonSerializable(typeof(List<ImprovementEntry>))]
[JsonSourceGenerationOptions(
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = false,
    AllowTrailingCommas = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    PropertyNameCaseInsensitive = true)]
public partial class ImprovementJsonContext : JsonSerializerContext;
