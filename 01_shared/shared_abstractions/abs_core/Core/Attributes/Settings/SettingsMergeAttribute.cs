namespace JoinCode.Abstractions.Attributes;

/// <summary>
/// 标记 Settings 类 — 源码生成器据此自动生成拷贝构造函数、Merge 方法、GetSettingByKey、UpdateSettingByKey
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class SettingsMergeAttribute : Attribute
{
}

/// <summary>
/// 标记 Settings 属性的合并策略 — 源码生成器据此决定 Merge 行为
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class SettingsPropertyAttribute : Attribute
{
    /// <summary>
    /// 合并策略
    /// </summary>
    public SettingsMergeStrategy Strategy { get; }

    /// <summary>
    /// 是否跳过拷贝构造函数（如 Schema 等元数据字段）
    /// </summary>
    public bool SkipCopy { get; init; }

    /// <summary>
    /// 是否跳过 Merge（如 Schema 等元数据字段）
    /// </summary>
    public bool SkipMerge { get; init; }

    /// <summary>
    /// 是否跳过 GetSettingByKey/UpdateSettingByKey（如复杂对象字段）
    /// </summary>
    public bool SkipKeyAccess { get; init; }

    /// <summary>
    /// 字典值类型名称（用于 Dictionary&lt;string, T&gt; 的深拷贝，如 "string"、"McpServerSettings"）
    /// </summary>
    public string? DictionaryValueType { get; init; }

    /// <summary>
    /// 自定义合并方法名（如 "MergePermissions"、"MergeHookDictionaries"）
    /// </summary>
    public string? CustomMergeMethod { get; init; }

    public SettingsPropertyAttribute(SettingsMergeStrategy strategy) => Strategy = strategy;
}

/// <summary>
/// Settings 属性合并策略
/// </summary>
public enum SettingsMergeStrategy
{
    /// <summary>
    /// 简单值覆盖: override ?? base（适用于 string?、bool?、int? 等 nullable 值类型）
    /// </summary>
    Override,

    /// <summary>
    /// 字典合并: 高优先级覆盖同键（适用于 Dictionary&lt;string, T&gt;）
    /// </summary>
    DictionaryMerge,

    /// <summary>
    /// 列表拼接去重（适用于 List&lt;string&gt;）
    /// </summary>
    ListConcatDistinct,

    /// <summary>
    /// 递归对象合并（适用于嵌套 Settings 对象）
    /// </summary>
    RecursiveMerge,

    /// <summary>
    /// 自定义合并（需指定 CustomMergeMethod）
    /// </summary>
    Custom,
}
