namespace Core.Utils;

public static class ValidationHelper
{
    public static string? ValidateRequired(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return $"{fieldName} 不能为空";
        }
        return null;
    }

    public static string? ValidateStringLength(string? value, int maxLength, string fieldName)
    {
        if (value != null && value.Length > maxLength)
        {
            return $"{fieldName} 过长";
        }
        return null;
    }

    public static string? ValidateRange(int? value, int min, int max, string fieldName)
    {
        if (value.HasValue && (value.Value < min || value.Value > max))
        {
            return $"{fieldName} 必须在 {min}-{max} 之间";
        }
        return null;
    }

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

    public static string? CombineErrors(params string?[] errors)
    {
        var nonNull = errors.Where(e => e != null).ToList();
        return nonNull.Count > 0 ? string.Join("; ", nonNull) : null;
    }
}
