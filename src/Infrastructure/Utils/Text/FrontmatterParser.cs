
namespace Core.Utils;

/// <summary>
/// Frontmatter解析结果
/// </summary>
public sealed class FrontmatterParseResult
{
    /// <summary>
    /// 是否包含frontmatter
    /// </summary>
    public bool HasFrontmatter { get; init; }

    /// <summary>
    /// 原始frontmatter内容（包含分隔符）
    /// </summary>
    public string RawFrontmatter { get; init; } = string.Empty;

    /// <summary>
    /// 解析后的frontmatter数据
    /// </summary>
    public Dictionary<string, JsonElement> Data { get; init; } = new();

    /// <summary>
    /// Markdown内容（不含frontmatter）
    /// </summary>
    public string Content { get; init; } = string.Empty;

    /// <summary>
    /// 解析错误信息（如果有）
    /// </summary>
    public string? Error { get; init; }
}

/// <summary>
/// 技能元数据
/// </summary>
public sealed class SkillMetadata
{
    /// <summary>
    /// 技能名称
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// 技能描述
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// 技能版本
    /// </summary>
    public string? Version { get; init; }

    /// <summary>
    /// 作者
    /// </summary>
    public string? Author { get; init; }

    /// <summary>
    /// 标签列表
    /// </summary>
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();

    /// <summary>
    /// 所需权限
    /// </summary>
    public IReadOnlyList<string> Permissions { get; init; } = Array.Empty<string>();

    /// <summary>
    /// 依赖项
    /// </summary>
    public IReadOnlyList<string> Dependencies { get; init; } = Array.Empty<string>();

    /// <summary>
    /// 额外属性
    /// </summary>
    public Dictionary<string, JsonElement> Extra { get; init; } = new();
}

/// <summary>
/// Frontmatter解析器
/// 解析YAML frontmatter，提取技能元数据
/// </summary>
public static class FrontmatterParser
{
    private const string FrontmatterDelimiter = "---";

    /// <summary>
    /// 解析Markdown文件的frontmatter
    /// </summary>
    /// <param name="markdown">Markdown内容</param>
    /// <returns>解析结果</returns>
    public static FrontmatterParseResult Parse(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return new FrontmatterParseResult
            {
                HasFrontmatter = false,
                Content = markdown ?? string.Empty
            };
        }

        var trimmedContent = markdown.TrimStart();

        if (!trimmedContent.StartsWith(FrontmatterDelimiter, StringComparison.Ordinal))
        {
            return new FrontmatterParseResult
            {
                HasFrontmatter = false,
                Content = markdown
            };
        }

        var endIndex = FindFrontmatterEnd(trimmedContent);

        if (endIndex == -1)
        {
            return new FrontmatterParseResult
            {
                HasFrontmatter = false,
                Content = markdown,
                Error = "未找到frontmatter结束标记"
            };
        }

        var frontmatterStart = FrontmatterDelimiter.Length;
        var frontmatterLength = endIndex - frontmatterStart;
        var frontmatterContent = trimmedContent.AsSpan(frontmatterStart, frontmatterLength).Trim().ToString();

        var contentStart = endIndex + FrontmatterDelimiter.Length;
        var content = trimmedContent[contentStart..].TrimStart();

        var data = ParseYaml(frontmatterContent);

        return new FrontmatterParseResult
        {
            HasFrontmatter = true,
            RawFrontmatter = trimmedContent[..(endIndex + FrontmatterDelimiter.Length)],
            Data = data,
            Content = content
        };
    }

    /// <summary>
    /// 异步解析Markdown文件的frontmatter
    /// </summary>
    /// <param name="markdown">Markdown内容</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>解析结果</returns>
    public static Task<FrontmatterParseResult> ParseAsync(string markdown, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => Parse(markdown), cancellationToken);
    }

    /// <summary>
    /// 从文件解析frontmatter
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="fs">文件系统抽象</param>
    /// <returns>解析结果</returns>
    public static FrontmatterParseResult ParseFile(string filePath, IFileSystem fs)
    {
        if (!fs.FileExists(filePath))
            throw new FileNotFoundException($"文件不存在: {filePath}");

        var content = fs.ReadAllText(filePath, Encoding.UTF8);
        return Parse(content);
    }

    /// <summary>
    /// 异步从文件解析frontmatter
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="fs">文件系统抽象</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>解析结果</returns>
    public static async Task<FrontmatterParseResult> ParseFileAsync(string filePath, IFileSystem fs, CancellationToken cancellationToken = default)
    {
        if (!fs.FileExists(filePath))
            throw new FileNotFoundException($"文件不存在: {filePath}");

        var content = await fs.ReadAllTextAsync(filePath, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        return Parse(content);
    }

    /// <summary>
    /// 提取技能元数据
    /// </summary>
    /// <param name="result">Frontmatter解析结果</param>
    /// <returns>技能元数据</returns>
    public static SkillMetadata ExtractSkillMetadata(FrontmatterParseResult result)
    {
        if (!result.HasFrontmatter || result.Data.Count == 0)
            return new SkillMetadata();

        var data = result.Data;

        return new SkillMetadata
        {
            Name = GetStringValue(data, "name", "title", "skill"),
            Description = GetStringValue(data, "description", "desc", "summary"),
            Version = GetStringValue(data, "version"),
            Author = GetStringValue(data, "author", "creator"),
            Tags = GetStringList(data, "tags", "tag", "keywords"),
            Permissions = GetStringList(data, "permissions", "requires"),
            Dependencies = GetStringList(data, "dependencies", "depends", "requires"),
            Extra = data.Where(kvp => !IsKnownKey(kvp.Key)).ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
        };
    }

    /// <summary>
    /// 将frontmatter数据序列化为YAML字符串
    /// </summary>
    /// <param name="data">数据字典</param>
    /// <returns>YAML字符串</returns>
    public static string Serialize(Dictionary<string, JsonElement> data)
    {
        if (data == null || data.Count == 0)
            return string.Empty;

        var yaml = new YamlMappingNode();

        foreach (var kvp in data)
        {
            yaml.Add(kvp.Key, ConvertToYamlNode(kvp.Value));
        }

        var serializer = new YamlDotNet.Serialization.Serializer();
        var yamlText = serializer.Serialize(yaml);

        return $"{FrontmatterDelimiter}\n{yamlText}{FrontmatterDelimiter}\n";
    }

    private static int FindFrontmatterEnd(string content)
    {
        var searchStart = FrontmatterDelimiter.Length;

        var endIndex = content.IndexOf(FrontmatterDelimiter, searchStart, StringComparison.Ordinal);

        if (endIndex == -1)
            return -1;

        for (var i = searchStart; i < endIndex; i++)
        {
            var lineEnd = content.IndexOf('\n', i);
            if (lineEnd == -1 || lineEnd > endIndex)
                lineEnd = endIndex;

            var trimmedLine = content.AsSpan(i, lineEnd - i).Trim();

            if (trimmedLine.SequenceEqual(FrontmatterDelimiter.AsSpan()))
            {
                if (i == 0 || content[i - 1] == '\n')
                    return i;
            }

            i = lineEnd;
        }

        return endIndex;
    }

    private static Dictionary<string, JsonElement> ParseYaml(string yamlContent)
    {
        var result = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var yaml = new YamlStream();
            yaml.Load(new StringReader(yamlContent));

            if (yaml.Documents.Count > 0 && yaml.Documents[0].RootNode is YamlMappingNode mapping)
            {
                foreach (var entry in mapping.Children)
                {
                    var key = entry.Key.ToString();
                    var value = ConvertYamlNodeToJsonElement(entry.Value);

                    if (key != null && value != null)
                        result[key] = value.Value;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"FrontmatterParser: failed to parse YAML frontmatter: {ex.Message}");
        }

        return result;
    }

    private static JsonElement? ConvertYamlNodeToJsonElement(YamlNode node)
    {
        var json = YamlNodeToJson(node);
        if (json == null) return null;
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        var reader = new Utf8JsonReader(bytes);
        using var doc = JsonDocument.ParseValue(ref reader);
        return doc.RootElement.Clone();
    }

    private static string? YamlNodeToJson(YamlNode node)
    {
        return node switch
        {
            YamlScalarNode scalar => ScalarToJson(scalar.Value),
            YamlSequenceNode sequence => "[" + string.Join(",", sequence.Children.Select(YamlNodeToJson).Where(v => v != null)) + "]",
            YamlMappingNode mapping => "{" + string.Join(",", mapping.Children.Select(kvp =>
            {
                var key = JsonEncodeString(kvp.Key.ToString() ?? string.Empty);
                var val = YamlNodeToJson(kvp.Value);
                return val != null ? $"{key}:{val}" : null;
            }).Where(v => v != null)) + "}",
            _ => JsonEncodeString(node.ToString())
        };
    }

    private static string ScalarToJson(string? value)
    {
        if (value == null) return "null";
        if (value.Equals("true", StringComparison.OrdinalIgnoreCase)) return "true";
        if (value.Equals("false", StringComparison.OrdinalIgnoreCase)) return "false";
        if (value.Equals("null", StringComparison.OrdinalIgnoreCase) || value.Equals("~", StringComparison.Ordinal)) return "null";
        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var intVal)) return intVal.ToString(CultureInfo.InvariantCulture);
        if (long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var longVal)) return longVal.ToString(CultureInfo.InvariantCulture);
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleVal)) return doubleVal.ToString(CultureInfo.InvariantCulture);
        return JsonEncodeString(value);
    }

    private static string JsonEncodeString(string s)
    {
        var sb = new StringBuilder(s.Length + 2);
        sb.Append('"');
        foreach (var c in s)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default: sb.Append(c); break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }

    private static YamlNode ConvertToYamlNode(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => new YamlScalarNode(element.GetString()),
            JsonValueKind.Number => new YamlScalarNode(element.GetRawText()),
            JsonValueKind.True => new YamlScalarNode("true"),
            JsonValueKind.False => new YamlScalarNode("false"),
            JsonValueKind.Null => new YamlScalarNode(null),
            JsonValueKind.Array => new YamlSequenceNode(element.EnumerateArray().Select(ConvertToYamlNode)),
            JsonValueKind.Object => new YamlMappingNode(
                element.EnumerateObject().Select(p =>
                    new KeyValuePair<YamlNode, YamlNode>(
                        new YamlScalarNode(p.Name),
                        ConvertToYamlNode(p.Value)))),
            _ => new YamlScalarNode(element.GetRawText())
        };
    }

    private static string? GetStringValue(Dictionary<string, JsonElement> data, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (data.TryGetValue(key, out var element) && element.ValueKind == JsonValueKind.String)
                return element.GetString();
        }
        return null;
    }

    private static IReadOnlyList<string> GetStringList(Dictionary<string, JsonElement> data, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!data.TryGetValue(key, out var element))
                continue;

            return element.ValueKind switch
            {
                JsonValueKind.String => new[] { element.GetString() ?? string.Empty },
                JsonValueKind.Array => element.EnumerateArray()
                    .Where(e => e.ValueKind == JsonValueKind.String)
                    .Select(e => e.GetString() ?? string.Empty)
                    .ToList(),
                _ => Array.Empty<string>()
            };
        }
        return Array.Empty<string>();
    }

    private static readonly FrozenSet<string> KnownKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "name", "title", "skill",
        "description", "desc", "summary",
        "version",
        "author", "creator",
        "tags", "tag", "keywords",
        "permissions", "requires",
        "dependencies", "depends"
    }.ToFrozenSet();

    private static bool IsKnownKey(string key) => KnownKeys.Contains(key);
}
