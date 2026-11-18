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
    public static IReadOnlyList<CliToolDescriptor> LoadNodesFromDirectory(string root) {
        if (!Directory.Exists(root)) return Array.Empty<CliToolDescriptor>();
        var nodes = Directory.EnumerateFiles(root, NodeFileName, SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(TryLoadNode)
            .OfType<CliToolDescriptor>()
            .ToArray();
        // G2: 目录内所有 node.yaml 互为继承池 —— 子节点的 inherits 在这里解析成最终属性。
        // 13 个真实分组都没有 inherits，ResolveAll 对它们是 no-op，零回归。
        return CliNodeInheritanceResolver.ResolveAll(nodes);
    }

    /// <summary>
    /// 单个清单损坏时跳过，而不是让整个搜索崩溃 — 几十个分组里任意一个写错都不该拖垮下钻。
    /// 报错含原因、触发文件、拦截层与修复方式，避免静默吞掉。
    /// </summary>
    private static CliToolDescriptor? TryLoadNode(string path) {
        try {
            return LoadNodeFromFile(path);
        } catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException) {
            Console.Error.WriteLine($"跳过损坏的 CLI 节点清单 '{path}'（NodeManifestLoader 目录扫描层拦截）：{ex.Message}");
            Console.Error.WriteLine("修复方式：检查该 node.yaml 的 id/name/summary 是否完整且 YAML 转义正确；或用 --dir 指向其他工具目录。");
            return null;
        }
    }
}
