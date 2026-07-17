namespace JoinCode.Abstractions.LLM.Chat;

public static class ContentHash
{
    private const int HexLength = 16;

    public static string Compute(string content)
    {
        var hash = global::System.Security.Cryptography.SHA256.HashData(
            global::System.Text.Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(hash)[..HexLength];
    }

    public static string ComputeToolSpecs(IReadOnlyList<ToolSpec> specs)
    {
        var sortedSpecs = specs
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ThenBy(t => t.Description, StringComparer.Ordinal)
            .ThenBy(t => t.InputSchemaJson, StringComparer.Ordinal);
        var blob = string.Join("|", sortedSpecs.Select(t =>
            $"{t.Name}:{t.Description}:{t.InputSchemaJson}"));
        return Compute(blob);
    }

    public static string ComputeToolNames(IReadOnlyList<ToolSpec> specs)
    {
        var blob = string.Join(",",
            specs.Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal));
        return Compute(blob);
    }
}
