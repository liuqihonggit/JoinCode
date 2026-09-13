namespace Structura.Primitives;

/// <summary>
/// 通用验证结果 — 替代各处重复的 IsValid+Message / Success+ErrorMessage 模式
/// </summary>
public sealed record ValidationResult
{
    /// <summary>是否通过验证</summary>
    public bool IsValid { get; init; }
    /// <summary>验证失败时的消息;通过时为 null</summary>
    public string? Message { get; init; }

    /// <summary>构造验证通过结果</summary>
    /// <returns>IsValid=true 的结果</returns>
    public static ValidationResult Valid() => new() { IsValid = true };
    /// <summary>构造验证失败结果</summary>
    /// <param name="message">失败原因描述</param>
    /// <returns>IsValid=false 且携带失败消息的结果</returns>
    public static ValidationResult Invalid(string message) => new() { IsValid = false, Message = message };

    /// <summary>解构为 (是否通过, 消息) 元组,便于模式匹配</summary>
    /// <param name="isValid">是否通过验证</param>
    /// <param name="message">验证消息</param>
    public void Deconstruct(out bool isValid, out string? message)
    {
        isValid = IsValid;
        message = Message;
    }
}
