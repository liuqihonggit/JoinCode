
namespace JoinCode.Abstractions.Security.Shell;

/// <summary>
/// Bash安全验证器 — 对齐 TS bashSecurity.ts 的核心安全检查
/// 检测命令注入、混淆、绕过等安全威胁
/// </summary>
public interface IBashSecurityValidator
{
    /// <summary>
    /// 验证命令安全性，返回验证结果
    /// </summary>
    BashSecurityResult Validate(string command);
}

/// <summary>
/// Bash安全验证结果
/// </summary>
public sealed record BashSecurityResult(
    bool IsSafe,
    BashSecurityCheckId? CheckId = null,
    string? Message = null,
    bool IsMisparsing = false);
