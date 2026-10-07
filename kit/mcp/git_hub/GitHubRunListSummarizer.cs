namespace McpToolDispatch;

/// <summary>
/// GitHub Run 列表 JSON 精简器 — 只保留关键字段，去掉冗余 URL
/// </summary>
internal static class GitHubRunListSummarizer {
    /// <summary>
    /// 精简 Actions Run 列表 JSON — 只保留关键字段，去掉冗余 URL，便于人类浏览和 AI 解析
    /// </summary>
    public static string SummarizeRunList(string json) {
        try {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return json;
            var root = doc.RootElement;
            if (!root.TryGetProperty("workflow_runs", out var runs) || runs.ValueKind != JsonValueKind.Array) return json;
            var buffer = new ArrayBufferWriter<byte>();
            using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping })) {
                writer.WriteStartObject();
                CopyProperty(root, writer, "total_count");
                writer.WritePropertyName("workflow_runs");
                writer.WriteStartArray();
                foreach (var run in runs.EnumerateArray()) {
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
        } catch (Exception) {
            return json;
        }
    }

    /// <summary>
    /// 把 run 列表 JSON 转人类可读表格 — gh 风格简洁输出(默认)
    /// </summary>
    public static string SummarizeRunListBrief(string json) {
        try {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return json;
            var root = doc.RootElement;
            if (!root.TryGetProperty("workflow_runs", out var runs) || runs.ValueKind != JsonValueKind.Array) return json;

            var totalCount = root.TryGetProperty("total_count", out var tc) && tc.TryGetInt32(out var t) ? t : runs.GetArrayLength();
            var sb = new StringBuilder(512);
            sb.AppendLine($"共 {totalCount} 个 run");
            sb.AppendLine();
            sb.AppendLine("S  ID           NUM   NAME                    BRANCH     EVENT       ELAPSED");
            sb.AppendLine("-  -----------  ----  ----------------------  --------   ---------   -------");

            foreach (var run in runs.EnumerateArray()) {
                var status = GitHubRunFormatHelper.GetString(run, "status");
                var conclusion = GitHubRunFormatHelper.GetString(run, "conclusion");
                var symbol = GitHubRunFormatHelper.GetStatusSymbol(status, conclusion);
                var id = GitHubRunFormatHelper.GetId(run) ?? "";
                var number = run.TryGetProperty("run_number", out var n) && n.TryGetInt32(out var num) ? num.ToString() : "";
                var name = GitHubRunFormatHelper.GetString(run, "name") ?? "";
                var branch = GitHubRunFormatHelper.GetString(run, "head_branch") ?? "";
                var evt = GitHubRunFormatHelper.GetString(run, "event") ?? "";
                var elapsed = GitHubRunFormatHelper.FormatElapsed(GitHubRunFormatHelper.GetString(run, "created_at"), GitHubRunFormatHelper.GetString(run, "updated_at")) ?? "";

                sb.Append(symbol).Append("  ");
                AppendFixedWidth(sb, id, 11); sb.Append("  ");
                AppendFixedWidth(sb, number, 4); sb.Append("  ");
                AppendFixedWidth(sb, name, 22); sb.Append("  ");
                AppendFixedWidth(sb, branch, 8); sb.Append("   ");
                AppendFixedWidth(sb, evt, 9); sb.Append("   ");
                sb.AppendLine(elapsed);
            }

            return sb.ToString().TrimEnd();
        } catch {
            return json;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AppendFixedWidth(StringBuilder sb, string value, int width) {
        if (value.Length <= width) {
            sb.Append(value);
            for (var i = value.Length; i < width; i++) sb.Append(' ');
        } else {
            sb.Append(value, 0, width - 1).Append('…');
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void CopyProperty(JsonElement source, Utf8JsonWriter writer, string name) {
        if (source.TryGetProperty(name, out var prop)) {
            writer.WritePropertyName(name);
            prop.WriteTo(writer);
        }
    }
}