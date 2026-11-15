namespace JoinCode.Abstractions.Tools;

/// <summary>基础工具在协议原生 tools 字段中的完整定义。</summary>
public sealed record BaseToolDefinition(string Name, string Description, ToolSchema Schema);

/// <summary>
/// 基础工具注册表 — 首轮请求 tools 字段的唯一来源。
/// 来源为 resources/tools/base.yaml 内嵌资源，取代 [McpToolDispatch] 特性 + 源码生成器。
/// </summary>
public sealed class BaseToolRegistry {
    private readonly FrozenDictionary<string, CliToolDescriptor> _byId;

    /// <summary>全部基础工具（保持 YAML 中的声明顺序）。</summary>
    public IReadOnlyList<CliToolDescriptor> All { get; }

    /// <summary>基础工具数量 — 回归守卫断言其恒为 4。</summary>
    public int Count => All.Count;

    private BaseToolRegistry(BaseToolManifest manifest) {
        var duplicate = manifest.Tools
            .GroupBy(tool => tool.Id, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException($"base.yaml 存在重复工具 id '{duplicate.Key}'");

        All = manifest.Tools;
        _byId = manifest.Tools.ToFrozenDictionary(tool => tool.Id, StringComparer.Ordinal);
    }

    public static BaseToolRegistry FromManifest(BaseToolManifest manifest) => new(manifest);

    public static BaseToolRegistry LoadDefault() =>
        FromManifest(NodeManifestLoader.LoadBaseFromResources());

    public CliToolDescriptor? Get(string id) => _byId.TryGetValue(id, out var descriptor) ? descriptor : null;

    /// <summary>转换为可直送供应商 tools 字段的定义列表。</summary>
    public IReadOnlyList<BaseToolDefinition> ToToolDefinitions() =>
        All.Select(tool => new BaseToolDefinition(tool.Name, tool.Summary, ToSchema(tool))).ToArray();

    private static ToolSchema ToSchema(CliToolDescriptor descriptor) =>
        new() {
            Properties = descriptor.Parameters.ToDictionary(
                parameter => parameter.Name,
                parameter => new ToolSchemaProperty {
                    Type = NormalizeType(parameter.Type),
                    Description = parameter.Description,
                    Default = parameter.Default,
                    Enum = SplitList(parameter.Enum)
                },
                StringComparer.Ordinal),
            Required = descriptor.Parameters
                .Where(parameter => parameter.Required)
                .Select(parameter => parameter.Name)
                .ToList()
        };

    private static string NormalizeType(string raw) => raw.ToLowerInvariant() switch {
        "int" or "integer" => "integer",
        "bool" or "boolean" => "boolean",
        "array" or "list" => "array",
        "number" or "float" or "double" => "number",
        _ => "string"
    };

    private static List<string>? SplitList(string? raw) =>
        string.IsNullOrWhiteSpace(raw)
            ? null
            : raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
}
