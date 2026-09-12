namespace Structura.Primitives;

/// <summary>
/// 通用验证结果 — 替代各处重复的 IsValid+Message / Success+ErrorMessage 模式
/// </summary>
public sealed record ValidationResult
{
    public bool IsValid { get; init; }
    public string? Message { get; init; }

    public static ValidationResult Valid() => new() { IsValid = true };
    public static ValidationResult Invalid(string message) => new() { IsValid = false, Message = message };

    public void Deconstruct(out bool isValid, out string? message)
    {
        isValid = IsValid;
        message = Message;
    }
}
