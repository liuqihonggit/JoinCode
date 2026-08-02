namespace JoinCode.Abstractions.Utils;

/// <summary>
/// REPL语言类型枚举
/// [EnumValue] 特性由 EnumMetadataGenerator 自动生成 ReplLanguageConstants + ReplLanguageExtensions
/// </summary>
public enum ReplLanguage
{
    /// <summary>C#语言</summary>
    [EnumValue("csharp")] CSharp = 0,

    /// <summary>PowerShell</summary>
    [EnumValue("powershell")] PowerShell = 1,

    /// <summary>Python</summary>
    [EnumValue("python")] Python = 2
}
