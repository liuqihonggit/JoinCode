namespace JoinCode.Abstractions.Attributes;

/// <summary>
/// 标记枚举成员为命令行选项 — 源码生成器据此自动生成解析代码、Schema、分层帮助
/// </summary>
/// <remarks>
/// 构造函数参数: longName → <see cref="LongName"/>, shortName → <see cref="ShortName"/>, description → <see cref="Description"/>
/// </remarks>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class CliOptionAttribute : Attribute
{
    /// <summary>长参数名（如 "--help"）</summary>
    public string LongName { get; }

    /// <summary>短参数名（如 "-h"），无短参数为空字符串</summary>
    public string ShortName { get; }

    /// <summary>中文描述</summary>
    public string Description { get; }

    /// <summary>是否接受值参数（如 --model &lt;id&gt;）</summary>
    public bool AcceptsValue { get; init; }

    /// <summary>是否为否定形式（如 --no-sandbox），生成器会将此标记映射到对应肯定形式的 bool 字段并设为 false</summary>
    public bool IsNegation { get; init; }

    /// <summary>
    /// 命令风险分级 — 对齐架构指南安全设计（read/write/dangerous）
    /// 生成器据此在 Schema 和 Help 中标注风险级别
    /// </summary>
    public string? RiskLevel { get; init; }

    /// <summary>
    /// 参数分类 — 生成器按分类分组输出帮助文本
    /// 如 "基础"、"会话"、"权限"、"输出"、"诊断"、"医生"
    /// </summary>
    public string? Category { get; init; }

    /// <summary>
    /// 使用示例 — 生成器在 --help=examples 中展示
    /// 如 "jcc -p \"hello\" --json"
    /// </summary>
    public string? Example { get; init; }

    public CliOptionAttribute(string longName, string shortName, string description)
    {
        LongName = longName ?? throw new ArgumentNullException(nameof(longName));
        ShortName = shortName ?? string.Empty;
        Description = description ?? throw new ArgumentNullException(nameof(description));
    }
}
