namespace Testing.Common;

/// <summary>
/// JSON 语义断言 — 解析后按路径/值断言，不依赖字符串排版与 Unicode 转义。
/// <para>背景：DTO 序列化（JCC1017）输出为紧凑 JSON 且非 ASCII 默认转义；
/// 断言 <c>Should().Contain("\"key\": value")</c> 依赖排版会随序列化细节变化集体红（TASK099 实测 9 例）。</para>
/// </summary>
public static class JsonAssert {
    /// <summary>解析 JSON — 非法时抛带输出片段说明的异常。</summary>
    public static JsonDocument Parse(string text) {
        ArgumentNullException.ThrowIfNull(text);
        try {
            return JsonDocument.Parse(text);
        } catch (JsonException ex) {
            throw new InvalidOperationException(
                $"输出不是合法 JSON（长度 {text.Length}）: {text[..Math.Min(text.Length, 200)]}", ex);
        }
    }

    /// <summary>按点路径取元素，支持 <c>a.b[0].c</c>；缺失时抛含路径与全文片段的异常。</summary>
    public static JsonElement Get(JsonElement root, string path) {
        var current = root;
        foreach (var segment in SplitPath(path)) {
            if (segment.StartsWith('[') && segment.EndsWith(']')) {
                var index = int.Parse(segment[1..^1], System.Globalization.CultureInfo.InvariantCulture);
                if (current.ValueKind != JsonValueKind.Array || index < 0 || index >= current.GetArrayLength())
                    throw new InvalidOperationException($"路径 '{path}' 处数组越界或非数组: {Describe(root)}");
                current = current[index];
                continue;
            }
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out var next))
                throw new InvalidOperationException($"路径 '{path}' 在 '{segment}' 处不存在: {Describe(root)}");
            current = next;
        }
        return current;
    }

    /// <summary>断言字符串属性值（含非 ASCII）。</summary>
    public static void AssertString(string json, string path, string expected, string? because = null) {
        using var doc = Parse(json);
        var actual = Get(doc.RootElement, path);
        if (actual.ValueKind != JsonValueKind.String || actual.GetString() != expected)
            throw new InvalidOperationException(
                $"路径 '{path}' 期望字符串 '{expected}'，实际 {actual.GetRawText()}{FormatBecause(because)}");
    }

    /// <summary>断言布尔属性值。</summary>
    public static void AssertBool(string json, string path, bool expected, string? because = null) {
        using var doc = Parse(json);
        var actual = Get(doc.RootElement, path);
        if (actual.ValueKind is not (JsonValueKind.True or JsonValueKind.False) || actual.GetBoolean() != expected)
            throw new InvalidOperationException(
                $"路径 '{path}' 期望 {expected}，实际 {actual.GetRawText()}{FormatBecause(because)}");
    }

    /// <summary>断言整数属性值。</summary>
    public static void AssertInt(string json, string path, int expected, string? because = null) {
        using var doc = Parse(json);
        var actual = Get(doc.RootElement, path);
        if (actual.ValueKind != JsonValueKind.Number || !actual.TryGetInt32(out var value) || value != expected)
            throw new InvalidOperationException(
                $"路径 '{path}' 期望 {expected}，实际 {actual.GetRawText()}{FormatBecause(because)}");
    }

    /// <summary>断言数组（按路径）中存在满足谓词的元素。</summary>
    public static void AssertArrayContains(string json, string arrayPath, Func<JsonElement, bool> predicate, string? because = null) {
        using var doc = Parse(json);
        var array = Get(doc.RootElement, arrayPath);
        if (array.ValueKind != JsonValueKind.Array || !array.EnumerateArray().Any(predicate))
            throw new InvalidOperationException(
                $"路径 '{arrayPath}' 未找到满足条件的元素，实际: {array.GetRawText()}{FormatBecause(because)}");
    }

    /// <summary>断言数组内对象的某字符串字段存在包含指定子串的项。</summary>
    public static void AssertAnyStringContains(string json, string arrayPath, string propertyName, string substring, string? because = null) =>
        AssertArrayContains(json, arrayPath,
            e => e.ValueKind == JsonValueKind.Object
                && e.TryGetProperty(propertyName, out var value)
                && value.ValueKind == JsonValueKind.String
                && value.GetString()?.Contains(substring, StringComparison.Ordinal) == true,
            because);

    private static IEnumerable<string> SplitPath(string path) {
        var buffer = new System.Text.StringBuilder();
        foreach (var ch in path) {
            if (ch == '[') {
                if (buffer.Length > 0) { yield return buffer.ToString(); buffer.Clear(); }
                buffer.Append(ch);
            } else if (ch == ']') {
                buffer.Append(ch);
                yield return buffer.ToString();
                buffer.Clear();
            } else if (ch == '.') {
                if (buffer.Length > 0) { yield return buffer.ToString(); buffer.Clear(); }
            } else {
                buffer.Append(ch);
            }
        }
        if (buffer.Length > 0) yield return buffer.ToString();
    }

    private static string Describe(JsonElement root) => root.GetRawText() is var text && text.Length > 300
        ? text[..300] + "…"
        : text;

    private static string FormatBecause(string? because) => string.IsNullOrEmpty(because) ? string.Empty : $"，原因: {because}";
}
