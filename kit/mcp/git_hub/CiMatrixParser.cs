namespace McpToolDispatch;

/// <summary>
/// CI yml matrix 解析器 — 从 ci-unit-tests.yml 的 matrix.include 列表提取测试 job 名
/// <para>单一职责:纯文本解析,无外部依赖,可独立测试</para>
/// <para>链式:ymlContent → ExtractMatrixJobNames → job 名列表</para>
/// </summary>
internal static class CiMatrixParser {

    /// <summary>
    /// 从 CI yml matrix.include 列表提取 name 字段值 — 简单行扫描, 不依赖 YamlDotNet
    /// </summary>
    /// <param name="ymlContent">CI yml 文件内容</param>
    /// <returns>matrix 中所有 - name: 的值(保序)</returns>
    internal static List<string> ExtractMatrixJobNames(string ymlContent) {
        var names = new List<string>();
        var inMatrix = false;
        foreach (var line in ymlContent.Split('\n')) {
            var trimmed = line.AsSpan().Trim();
            if (trimmed.StartsWith("matrix:")) { inMatrix = true; continue; }
            if (inMatrix && trimmed.StartsWith("- name:")) {
                var value = trimmed[7..].Trim().ToString();
                if (!string.IsNullOrEmpty(value)) names.Add(value);
            }
            if (inMatrix && trimmed.Length > 0 && !trimmed.StartsWith("-") && !trimmed.StartsWith("name:") && !trimmed.StartsWith("#") && !char.IsWhiteSpace(line[0]))
                inMatrix = false;
        }
        return names;
    }
}
