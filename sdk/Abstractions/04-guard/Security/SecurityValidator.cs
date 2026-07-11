namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 代码安全验证结果
/// </summary>
public sealed record CodeSecurityValidationResult(bool IsValid, string Message);

/// <summary>
/// 代码安全验证器接口 - 验证代码是否包含危险操作
/// </summary>
public interface ICodeSecurityValidator
{
    /// <summary>
    /// 验证代码安全性
    /// </summary>
    /// <param name="code">要验证的代码</param>
    /// <param name="allowExternalLibs">是否允许使用外部库</param>
    /// <returns>验证结果</returns>
    CodeSecurityValidationResult Validate(string code, bool allowExternalLibs);
}
