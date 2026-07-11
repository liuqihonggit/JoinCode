namespace JccAuditCli;

/// <summary>
/// 解析 .slnx XML 格式，提取项目路径列表
/// MSBuildWorkspace 不支持 .slnx，需要手动解析后逐个加载 .csproj
/// </summary>
public static class SlnxParser
{
    /// <summary>
    /// 从 .slnx 文件中提取所有 .csproj 路径（相对于 .slnx 所在目录）
    /// </summary>
    public static List<string> ParseProjectPaths(string slnxPath)
    {
        var slnxDir = Path.GetDirectoryName(Path.GetFullPath(slnxPath))!;
        var content = File.ReadAllText(slnxPath);
        var paths = new List<string>();

        // 简单 XML 解析：提取所有 <Project Path="..." /> 节点
        // 不用 System.Xml 以减少依赖和 AOT 问题
        var reader = new StringReader(content);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("<Project ", StringComparison.Ordinal))
                continue;

            var pathAttr = ExtractAttribute(trimmed, "Path");
            if (pathAttr is null)
                continue;

            var fullPath = Path.GetFullPath(Path.Combine(slnxDir, pathAttr));
            if (File.Exists(fullPath))
            {
                paths.Add(fullPath);
            }
            else
            {
                Console.WriteLine($"  警告: .slnx 中引用的项目不存在: {pathAttr}");
            }
        }

        return paths;
    }

    /// <summary>
    /// 从 XML 片段中提取指定属性的值
    /// </summary>
    private static string? ExtractAttribute(string xmlFragment, string attributeName)
    {
        var searchStr = attributeName + "=\"";
        var startIdx = xmlFragment.IndexOf(searchStr, StringComparison.Ordinal);
        if (startIdx < 0)
            return null;

        startIdx += searchStr.Length;
        var endIdx = xmlFragment.IndexOf('"', startIdx);
        if (endIdx < 0)
            return null;

        return xmlFragment[startIdx..endIdx];
    }
}
