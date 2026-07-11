namespace McpToolRegistry;

internal static class InputSchemaValidationFormatter
{
    public static string FormatErrors(string toolName, IReadOnlyList<ValidationError> errors)
    {
        if (errors.Count == 0) return string.Empty;

        var parts = new List<string>(errors.Count);
        foreach (var error in errors)
        {
            var formatted = FormatSingleError(error);
            parts.Add(formatted);
        }

        var issueWord = parts.Count == 1 ? "issue" : "issues";
        var details = string.Join("\n", parts);
        return $"{toolName} failed due to the following {issueWord}:\n{details}";
    }

    private static string FormatSingleError(ValidationError error)
    {
        var msg = error.Message;
        var path = error.Path;

        if (TryParseMissingRequired(msg, path, out var paramName))
        {
            return $"The required parameter `{paramName}` is missing";
        }

        if (TryParseUnexpectedKey(msg, out var unexpectedParam))
        {
            return $"An unexpected parameter `{unexpectedParam}` was provided";
        }

        if (TryParseTypeMismatch(msg, path, out var typeParam, out var expected, out var received))
        {
            return $"The parameter `{typeParam}` type is expected as `{expected}` but provided as `{received}`";
        }

        if (!string.IsNullOrEmpty(path) && path != "$")
        {
            var cleanPath = path.StartsWith("$.") ? path[2..] : path;
            return $"`{cleanPath}`: {msg}";
        }

        return msg;
    }

    private static bool TryParseMissingRequired(string msg, string path, out string paramName)
    {
        paramName = string.Empty;

        if (msg.Contains("missing", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("required", StringComparison.OrdinalIgnoreCase))
        {
            paramName = ExtractParamName(msg, path);
            return true;
        }

        return false;
    }

    private static bool TryParseUnexpectedKey(string msg, out string paramName)
    {
        paramName = string.Empty;

        if (msg.Contains("unexpected", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("unrecognized", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("additional", StringComparison.OrdinalIgnoreCase))
        {
            var idx = msg.IndexOf('\'');
            if (idx >= 0)
            {
                var endIdx = msg.IndexOf('\'', idx + 1);
                if (endIdx > idx)
                {
                    paramName = msg.Substring(idx + 1, endIdx - idx - 1);
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryParseTypeMismatch(string msg, string path, out string paramName, out string expected, out string received)
    {
        paramName = string.Empty;
        expected = string.Empty;
        received = string.Empty;

        if (msg.Contains("type", StringComparison.OrdinalIgnoreCase) &&
            (msg.Contains("expected", StringComparison.OrdinalIgnoreCase) ||
             msg.Contains("but got", StringComparison.OrdinalIgnoreCase)))
        {
            paramName = ExtractParamName(msg, path);

            var expectedIdx = msg.IndexOf("expected", StringComparison.OrdinalIgnoreCase);
            if (expectedIdx >= 0)
            {
                var sub = msg[expectedIdx..];
                var butIdx = sub.IndexOf("but", StringComparison.OrdinalIgnoreCase);
                if (butIdx > 0)
                {
                    expected = sub[..butIdx].Replace("expected", "").Trim(' ', '`');
                    received = sub[butIdx..].Replace("but got", "").Replace("but", "").Trim(' ', '`');
                    return true;
                }
            }
        }

        return false;
    }

    private static string ExtractParamName(string msg, string path)
    {
        if (!string.IsNullOrEmpty(path) && path != "$")
        {
            var cleanPath = path.StartsWith("$.") ? path[2..] : path;
            var dotIdx = cleanPath.IndexOf('.');
            if (dotIdx > 0) cleanPath = cleanPath[..dotIdx];
            return cleanPath;
        }

        var idx = msg.IndexOf('\'');
        if (idx >= 0)
        {
            var endIdx = msg.IndexOf('\'', idx + 1);
            if (endIdx > idx) return msg.Substring(idx + 1, endIdx - idx - 1);
        }

        return "unknown";
    }
}
