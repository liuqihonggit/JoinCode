namespace McpToolDispatch;

/// <summary>
/// GitHub Run 列表 JSON 精简器 — 只保留关键字段，去掉冗余 URL
/// </summary>
internal static class GitHubRunListSummarizer
{
    /// <summary>
    /// 精简 Actions Run 列表 JSON — 只保留关键字段，去掉冗余 URL，便于人类浏览和 AI 解析
    /// </summary>
    public static string SummarizeRunList(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return json;
            var root = doc.RootElement;
            if (!root.TryGetProperty("workflow_runs", out var runs) || runs.ValueKind != JsonValueKind.Array) return json;
            var buffer = new ArrayBufferWriter<byte>();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartObject();
                CopyProperty(root, writer, "total_count");
                writer.WritePropertyName("workflow_runs");
                writer.WriteStartArray();
                foreach (var run in runs.EnumerateArray())
                {
                    writer.WriteStartObject();
                    CopyProperty(run, writer, "id");
                    CopyProperty(run, writer, "name");
                    CopyProperty(run, writer, "head_branch");
                    CopyProperty(run, writer, "status");
                    CopyProperty(run, writer, "conclusion");
                    CopyProperty(run, writer, "run_number");
                    CopyProperty(run, writer, "created_at");
                    CopyProperty(run, writer, "html_url");
                    CopyProperty(run, writer, "display_title");
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            return Encoding.UTF8.GetString(buffer.WrittenSpan);
        }
        catch (Exception)
        {
            return json;
        }
    }

    private static void CopyProperty(JsonElement source, Utf8JsonWriter writer, string name)
    {
        if (source.TryGetProperty(name, out var prop))
        {
            writer.WritePropertyName(name);
            prop.WriteTo(writer);
        }
    }
}
