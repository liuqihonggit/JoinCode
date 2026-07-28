
namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 任务ID生成器
/// </summary>
public static class TaskIdGenerator
{
    // 大小写不敏感的字母表（数字+小写字母）
    private const string Alphabet = "0123456789abcdefghijklmnopqrstuvwxyz";
    private const int IdLength = 8;

    /// <summary>
    /// 生成任务ID
    /// </summary>
    public static string GenerateTaskId(TaskType type)
    {
        var prefix = TaskIdPrefixes.GetPrefix(type);
        var randomPart = GenerateRandomPart();
        return $"{prefix}{randomPart}";
    }

    /// <summary>
    /// 生成随机部分（8字符）
    /// </summary>
    private static string GenerateRandomPart()
    {
        Span<byte> bytes = stackalloc byte[IdLength];
        RandomNumberGenerator.Fill(bytes);

        Span<char> chars = stackalloc char[IdLength];
        for (var i = 0; i < IdLength; i++)
        {
            chars[i] = Alphabet[bytes[i] % Alphabet.Length];
        }

        return new string(chars);
    }
}
