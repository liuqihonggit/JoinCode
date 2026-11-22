namespace Core.Agents;

/// <summary>
/// Worktree JSON 格式化辅助 — 封装 <see cref="JsonNode"/> 的缩进序列化逻辑。
/// <para>用于持久化 worktree 会话到本地设置文件，输出缩进美观的 JSON 文本。</para>
/// <para>JsonWriterOptions 缓存为静态只读字段，避免每次格式化重复创建。</para>
/// </summary>
internal static class WorktreeJsonFormatting {
    private static readonly JsonWriterOptions s_indentedWriterOptions = new() { Indented = true };

    /// <summary>
    /// 将 <see cref="JsonNode"/> 格式化为缩进 JSON 字符串（UTF-8 编码）。
    /// </summary>
    /// <param name="node">待格式化的 JSON 节点</param>
    /// <returns>缩进格式的 JSON 字符串</returns>
    public static async Task<string> FormatJsonNode(JsonNode node) {
        await using var stream = new MemoryStream();
        await using var writer = new Utf8JsonWriter(stream, s_indentedWriterOptions);
        node.WriteTo(writer);
        writer.Flush();
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }
}