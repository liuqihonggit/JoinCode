using System.Reflection;

namespace JoinCode.Abstractions.Tools;

/// <summary>
/// CLI 节点清单加载入口 — 基础工具走内嵌资源（AOT 兼容），独立工具走磁盘 node.yaml。
/// 目录扫描是 CLI 层搜索下钻的数据来源，不经过 MCP、不进入提示词。
/// </summary>
public static class NodeManifestLoader {
    private const string ResourcePrefix = "JoinCode.CliNodes.";
    private const string BaseManifestName = "base.yaml";
    private const string NodeFileName = "node.yaml";

    /// <summary>解析 base.yaml 文本。</summary>
    public static BaseToolManifest LoadBaseFromText(string yaml, string source = "<text>") =>
        NodeYamlParser.ParseBase(yaml, source);

    /// <summary>解析单个 node.yaml 文本。</summary>
    public static CliToolDescriptor LoadNodeFromText(string yaml, string source = "<text>") =>
        NodeYamlParser.ParseNode(yaml, source);

    /// <summary>从程序集内嵌资源加载基础工具清单。</summary>
    public static BaseToolManifest LoadBaseFromResources(Assembly? assembly = null) {
        var asm = assembly ?? typeof(NodeManifestLoader).Assembly;
        var resourceName = ResourcePrefix + BaseManifestName;
        using var stream = asm.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"未找到内嵌资源 '{resourceName}'，请检查 csproj 的 EmbeddedResource 声明");
        using var reader = new StreamReader(stream);
        return LoadBaseFromText(reader.ReadToEnd(), resourceName);
    }

    /// <summary>从磁盘加载单个节点描述。</summary>
    public static CliToolDescriptor LoadNodeFromFile(string path) =>
        LoadNodeFromText(File.ReadAllText(path), path);

    /// <summary>
    /// 递归扫描目录树下所有 node.yaml — CLI 层下钻探索的数据源。
    /// 目录结构即分类，不需要 MCP 分组协议。
    /// </summary>
    public static IReadOnlyList<CliToolDescriptor> LoadNodesFromDirectory(string root) =>
        Directory.Exists(root)
            ? Directory.EnumerateFiles(root, NodeFileName, SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(LoadNodeFromFile)
                .ToArray()
            : Array.Empty<CliToolDescriptor>();
}
