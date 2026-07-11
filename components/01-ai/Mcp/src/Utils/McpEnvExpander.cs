using System.Text.RegularExpressions;

namespace McpClient;

public static partial class McpEnvExpander
{
    public static (string Expanded, List<string> MissingVars) ExpandEnvVarsInString(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var missingVars = new List<string>();

        var expanded = EnvVarRegex().Replace(value, match =>
        {
            var varContent = match.Groups[1].Value;

            var separatorIndex = varContent.IndexOf(":-", StringComparison.Ordinal);
            string varName;
            string? defaultValue = null;

            if (separatorIndex >= 0)
            {
                varName = varContent[..separatorIndex];
                defaultValue = varContent[(separatorIndex + 2)..];
            }
            else
            {
                varName = varContent;
            }

            var envValue = Environment.GetEnvironmentVariable(varName);

            if (envValue is not null)
            {
                return envValue;
            }

            if (defaultValue is not null)
            {
                return defaultValue;
            }

            missingVars.Add(varName);
            return match.Value;
        });

        return (expanded, missingVars);
    }

    public static Dictionary<string, string> ExpandEnvironmentValues(Dictionary<string, string>? environment)
    {
        if (environment is null || environment.Count == 0)
        {
            return environment ?? new Dictionary<string, string>();
        }

        var expanded = new Dictionary<string, string>(environment.Count);
        foreach (var kvp in environment)
        {
            if (kvp.Value.Contains('$'))
            {
                var (value, _) = ExpandEnvVarsInString(kvp.Value);
                expanded[kvp.Key] = value;
            }
            else
            {
                expanded[kvp.Key] = kvp.Value;
            }
        }

        return expanded;
    }

    public static string ExpandEndpoint(string endpoint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);

        if (!endpoint.Contains('$'))
        {
            return endpoint;
        }

        var (expanded, _) = ExpandEnvVarsInString(endpoint);
        return expanded;
    }

    [GeneratedRegex(@"\$\{([^}]+)\}", RegexOptions.Compiled)]
    private static partial Regex EnvVarRegex();
}