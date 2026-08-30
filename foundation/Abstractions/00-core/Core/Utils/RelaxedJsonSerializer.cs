namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 通用 JSON 序列化 helper — 在源码生成 JsonSerializerContext 基础上注入
/// UnsafeRelaxedJsonEscaping Encoder，使中文字符以真实 UTF-8 输出而非 \uXXXX 转义。
/// 源码生成器 attribute 无法引用静态 Encoder 属性，故在此运行时创建 options 副本。
/// 命名策略（CamelCase 等）与缩进由各 JsonSourceGenerationOptions 声明，副本继承。
/// </summary>
public static class RelaxedJsonSerializer
{
    /// <summary>
    /// 基于给定 JsonSerializerContext 创建带真实中文输出的 JsonSerializerOptions 副本。
    /// 保留原 context 的命名策略、缩进、注释处理等设置，仅替换 Encoder 与 TypeInfoResolver。
    /// 调用方应缓存返回值以避免每次序列化重复创建 options 对象。
    /// </summary>
    public static JsonSerializerOptions RelaxedOptions(this JsonSerializerContext context)
        => new(context.Options) { TypeInfoResolver = context, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>序列化为 JSON 字符串（真实中文，命名策略与缩进由 options 决定）。TypeInfoResolver 为源码生成，AOT 安全。</summary>
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "TypeInfoResolver 为源码生成的 JsonSerializerContext，所有类型已静态 rooted，AOT 安全。")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "TypeInfoResolver 为源码生成的 JsonSerializerContext，无需运行时反射 emit。")]
    public static string Serialize<T>(T value, JsonSerializerOptions options) => JsonSerializer.Serialize(value, options);

    /// <summary>序列化为 JSON 字符串（真实中文），直接从 context 创建 RelaxedOptions。适合低频调用；高频路径请缓存 RelaxedOptions()。</summary>
    public static string Serialize<T>(T value, JsonSerializerContext context) => Serialize(value, context.RelaxedOptions());
}
