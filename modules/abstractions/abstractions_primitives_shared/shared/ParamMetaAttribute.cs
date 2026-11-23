namespace JoinCode.Abstractions.Attributes;

/// <summary>
/// 标记 WellKnownParam 枚举成员的参数元数据 — 描述/必填/默认值/类型。
/// 源码生成器（param_metadata.generator）据此生成 WellKnownParamDescriptions 常量类。
/// </summary>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class ParamMetaAttribute : Attribute {
    /// <summary>获取参数描述文本。</summary>
    public string Description { get; }
    /// <summary>获取或设置是否必填（默认 true）。</summary>
    public bool Required { get; init; } = true;
    /// <summary>获取或设置默认值字符串表示。</summary>
    public string? DefaultValue { get; init; }
    /// <summary>获取或设置 JSON Schema 类型名（string/int/boolean 等）。</summary>
    public string? TypeName { get; init; }

    /// <summary>构造参数元数据特性。</summary>
    /// <param name="description">参数描述文本。</param>
    public ParamMetaAttribute(string description) {
        Description = description ?? throw new ArgumentNullException(nameof(description));
    }
}
