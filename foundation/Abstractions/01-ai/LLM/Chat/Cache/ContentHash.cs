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

    public static string ComputeToolSpecs(IEnumerable<ToolSpec> specs)
    {
        var sortedSpecs = specs
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ThenBy(t => t.Description, StringComparer.Ordinal)
            .ThenBy(t => t.InputSchemaJson, StringComparer.Ordinal);
        var blob = string.Join("|", sortedSpecs.Select(t =>
            $"{t.Name}:{t.Description}:{t.InputSchemaJson}"));
        return Compute(blob);
    }

    public static string ComputeToolNames(IEnumerable<ToolSpec> specs)
    {
        var blob = string.Join(",",
            specs.Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal));
        return Compute(blob);
    }

    /// <summary>
    /// 计算对话消息序列的联合 hash — 用于客户端检测"入站消息序列"前缀漂移。
    ///
    /// 与 MockServer TokenEstimator.ExtractConversationPrefix 保持一致的逐条编码语义:
    /// 每条消息编码为 role(\x01历元 + \x01 content \x00，按顺序拼接。
    /// 这样客户端检测对中途篡改/插入的判定, 与真实线上按完整对话前缀计算的缓存命中一致。
    /// </summary>
    public static string ComputeConversation(IEnumerable<ApiMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        var builder = new StringBuilder();
        foreach (var m in messages)
        {
            builder.Append(m.Role.ToValue());
            builder.Append('\x01');
            builder.Append(m.Content);
            builder.Append('\x00');
        }
        return Compute(builder.ToString());
    }
}
