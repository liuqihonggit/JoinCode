namespace Core.Utils;

/// <summary>
/// 参数校验辅助工具 — 提供必填、长度、范围、URL 校验,返回错误消息而非抛异常
/// </summary>
public static class ValidationHelper
{
    /// <summary>
    /// 校验必填字段
    /// </summary>
    /// <param name="value">待校验值</param>
    /// <param name="fieldName">字段名,用于错误消息</param>
    /// <returns>校验失败返回错误消息,通过返回 null</returns>
    public static string? ValidateRequired(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return $"{fieldName} 不能为空";
        }
        return null;
    }

    /// <summary>
    /// 校验字符串长度上限
    /// </summary>
    /// <param name="value">待校验值</param>
    /// <param name="maxLength">最大长度</param>
    /// <param name="fieldName">字段名,用于错误消息</param>
    /// <returns>校验失败返回错误消息,通过返回 null</returns>
    public static string? ValidateStringLength(string? value, int maxLength, string fieldName)
    {
        if (value != null && value.Length > maxLength)
        {
            return $"{fieldName} 过长";
        }
        return null;
    }

    /// <summary>
    /// 校验整数范围
    /// </summary>
    /// <param name="value">待校验值</param>
    /// <param name="min">最小值(含)</param>
    /// <param name="max">最大值(含)</param>
    /// <param name="fieldName">字段名,用于错误消息</param>
    /// <returns>校验失败返回错误消息,通过返回 null</returns>
    public static string? ValidateRange(int? value, int min, int max, string fieldName)
    {
        if (value.HasValue && (value.Value < min || value.Value > max))
        {
            return $"{fieldName} 必须在 {min}-{max} 之间";
        }
        return null;
    }

    /// <summary>
    /// 校验 URL 格式 — 必须为 http/https 绝对 URI
    /// </summary>
    /// <param name="value">待校验值</param>
    /// <param name="fieldName">字段名,用于错误消息</param>
    /// <returns>校验失败返回错误消息,通过或空值返回 null</returns>
    public static string? ValidateUrl(string? value, string fieldName)
    {
        if (!string.IsNullOrWhiteSpace(value) &&
            (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
             (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)))
        {
            return $"无效的 {fieldName} 格式";
        }
        return null;
    }

    /// <summary>
    /// 合并多个校验错误消息
    /// </summary>
    /// <param name="errors">错误消息数组,null 元素被忽略</param>
    /// <returns>用分号连接的错误消息;无错误返回 null</returns>
    public static string? CombineErrors(params string?[] errors)
    {
        var nonNull = errors.Where(e => e != null).ToList();
        return nonNull.Count > 0 ? string.Join("; ", nonNull) : null;
    }
}
