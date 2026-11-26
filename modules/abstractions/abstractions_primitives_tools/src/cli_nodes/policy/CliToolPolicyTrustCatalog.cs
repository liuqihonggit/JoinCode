namespace JoinCode.Abstractions.Tools;

/// <summary>
/// 策略信任目录 — 构建期生成，jcc 内嵌；broker 用它校验目标 exe 内嵌策略的 id 与内容哈希（ADR 0168）。
/// <para>没有目录锚定时，一个换了策略的 exe 只要文件名对得上就能被授权；有目录后，
/// 策略内容或文件名偏离构建产物都会 fail-closed。</para>
/// </summary>
internal sealed class CliToolPolicyTrustCatalog {
    private const string ResourceName = "JoinCode.CliNodes.policy_trust.yaml";

    private sealed record TrustEntry(string Hash, string[] Executables);

    private readonly FrozenDictionary<string, TrustEntry> _entries;

    private CliToolPolicyTrustCatalog(FrozenDictionary<string, TrustEntry> entries) => _entries = entries;

    /// <summary>内嵌信任目录；资源缺失或损坏时访问即抛，由调用方按拒绝处理。</summary>
    public static CliToolPolicyTrustCatalog Default { get; } = LoadDefault();

    /// <summary>校验策略身份；通过返回 null，否则返回拒绝原因。</summary>
    /// <param name="policyId">策略 ID。</param>
    /// <param name="sha256Hex">目标镜像内嵌策略的 SHA-256 十六进制。</param>
    /// <param name="executableName">执行镜像文件名。</param>
    /// <returns>拒绝原因为 null 表示可信。</returns>
    public string? Verify(string policyId, string sha256Hex, string executableName) {
        if (!_entries.TryGetValue(policyId, out var entry)) {
            return $"策略 '{policyId}' 不在信任目录中（可能是伪造 exe，或策略生成后忘记刷新 policy_trust.yaml）";
        }
        if (!string.Equals(entry.Hash, sha256Hex, StringComparison.OrdinalIgnoreCase)) {
            return $"策略 '{policyId}' 内容哈希不匹配：目录 {entry.Hash}，实际 {sha256Hex}";
        }
        if (!entry.Executables.Contains(executableName, StringComparer.OrdinalIgnoreCase)) {
            return $"执行文件 '{executableName}' 不在策略 '{policyId}' 的信任清单中";
        }
        return null;
    }

    private static CliToolPolicyTrustCatalog LoadDefault() {
        var assembly = typeof(CliToolPolicyTrustCatalog).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"未找到内嵌信任目录 '{ResourceName}'，检查 Tools csproj 的 EmbeddedResource");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    /// <summary>解析信任目录 YAML（测试与生成期对账用）。</summary>
    /// <param name="yaml">YAML 文本。</param>
    /// <returns>信任目录。</returns>
    public static CliToolPolicyTrustCatalog Parse(string yaml) {
        var document = new YamlStream();
        try {
            document.Load(new StringReader(yaml));
        } catch (YamlException ex) {
            throw new InvalidOperationException($"信任目录 YAML 解析失败: {ex.Message}", ex);
        }

        if (document.Documents.Count == 0
            || document.Documents[0].RootNode is not YamlMappingNode root
            || !root.Children.TryGetValue(new YamlScalarNode("policies"), out var policiesNode)
            || policiesNode is not YamlSequenceNode sequence) {
            throw new InvalidOperationException("信任目录缺少 policies 序列");
        }

        var entries = new Dictionary<string, TrustEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var child in sequence.Children) {
            if (child is not YamlMappingNode map) {
                throw new InvalidOperationException("信任目录的 policies 条目必须是映射");
            }
            var id = Scalar(map, "id");
            var hash = Scalar(map, "hash");
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(hash)) {
                throw new InvalidOperationException("信任目录条目缺少 id 或 hash");
            }
            if (hash.Length != 64 || !hash.All(Uri.IsHexDigit)) {
                throw new InvalidOperationException($"信任目录 '{id}' 的 hash 不是 64 位十六进制: '{hash}'");
            }
            if (map.Children.TryGetValue(new YamlScalarNode("executables"), out var exeNode)
                && exeNode is YamlSequenceNode exeSequence) {
                var executables = exeSequence.Children
                    .OfType<YamlScalarNode>()
                    .Select(node => node.Value)
                    .OfType<string>()
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .ToArray();
                entries[id] = new TrustEntry(hash, executables);
            } else {
                throw new InvalidOperationException($"信任目录 '{id}' 缺少 executables 序列");
            }
        }

        if (entries.Count == 0) {
            throw new InvalidOperationException("信任目录没有任何策略条目");
        }
        return new CliToolPolicyTrustCatalog(entries.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase));
    }

    private static string? Scalar(YamlMappingNode map, string key) =>
        map.Children.TryGetValue(new YamlScalarNode(key), out var node) && node is YamlScalarNode scalar
            ? scalar.Value
            : null;
}
