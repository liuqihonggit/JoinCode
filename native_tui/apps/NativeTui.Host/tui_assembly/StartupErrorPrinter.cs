namespace NativeTui.Host;

/// <summary>
/// 启动配置错误友好呈现（修复：配置错误直接闪退刷未处理异常堆栈）—
/// ConfigurationException → 结构化指引文本：标题+原因 → 检查点（两级 profile 覆盖链 +
/// auth key 状态 + 环境变量）→ 修复建议。错误诱导四要素（AGENTS.md 规矩9）：
/// 为什么失败 / 什么参数触发 / 当前状态在哪 / 接下来怎么做。
/// Probe 纯函数（注入 JSON 文本，测试友好）+ ProbeLive 真实读取（Main 消费）。
/// </summary>
internal static class StartupErrorPrinter
{
    /// <summary>探测结果 — 两级 profile 指针 + auth key 长度 + 环境变量名/值。</summary>
    /// <param name="UserProfile">用户级 ~/.jcc/settings.json 的 current.profile（缺/解析失败 null）。</param>
    /// <param name="ProjectProfile">项目级 {cwd}/.jcc/settings.json 的 current.profile（无文件 null — 覆盖链中断点）。</param>
    /// <param name="AuthVendor">探测的供应商标识。</param>
    /// <param name="AuthKeyLength">auth.json 对应键长度（0 = 无键或空串）。</param>
    /// <param name="EnvVarName">供应商专属环境变量名（ProviderEnvVar 映射）。</param>
    /// <param name="EnvVarValue">环境变量值（null = 未设置）。</param>
    internal sealed record ProbeResult(
        string? UserProfile,
        string? ProjectProfile,
        string AuthVendor,
        int AuthKeyLength,
        string? EnvVarName,
        string? EnvVarValue);

    /// <summary>错误标题（框线首行）。</summary>
    private const string Title = "jcc 启动失败 — 配置错误";

    /// <summary>auth.json 绝对路径（修复指引展示）。</summary>
    private static string AuthPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".jcc", "auth.json");

    /// <summary>用户级 settings.json 绝对路径。</summary>
    private static string UserSettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".jcc", "settings.json");

    /// <summary>项目级 settings.json 路径（cwd 下）。</summary>
    private static string ProjectSettingsPath => Path.Combine(
        Environment.CurrentDirectory, ".jcc", "settings.json");

    /// <summary>组装友好文本 — 框线面板：标题 / 原因 / 检查点（覆盖链 + key 状态）/ 修复建议。</summary>
    internal static string Format(ConfigurationException exception, ProbeResult probe)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"╭─ {Title}");
        builder.AppendLine($"│ {exception.Message}");
        builder.AppendLine("│");
        builder.AppendLine("│ 检查点（profile 覆盖链: 项目级 > 用户级 > 环境变量）:");
        AppendProfileLine(builder, "项目级", probe.ProjectProfile, ProjectSettingsPath);
        AppendProfileLine(builder, "用户级", probe.UserProfile, UserSettingsPath);
        builder.AppendLine($"│   API Key: {AuthPath} 的 '{probe.AuthVendor}' 键 " +
            (probe.AuthKeyLength > 0 ? $"已设置（{probe.AuthKeyLength} 字符）" : "缺失或为空"));
        if (probe.EnvVarName is { Length: > 0 } envName)
        {
            builder.AppendLine($"│   环境变量 {envName}: " + (probe.EnvVarValue is { Length: > 0 } ? "已设置" : "未设置"));
        }

        builder.AppendLine("│");
        builder.AppendLine("│ 修复建议:");
        if (probe.AuthKeyLength == 0)
        {
            builder.AppendLine($"│   A. 补充 key: 在 {AuthPath} 添加 \"{probe.AuthVendor}\": \"sk-...\"");
            if (probe.EnvVarName is { Length: > 0 } envName2)
            {
                builder.AppendLine($"│   B. 临时: 设置环境变量 {envName2}");
            }
        }

        builder.AppendLine("│   C. 切换到已有 key 的供应商: 编辑 settings.json 的 current.profile");
        builder.AppendLine("│      （指向 auth.json 中已有非空键的供应商，或用 /vendor 切换后重启）");
        builder.Append("╰─");
        return builder.ToString();
    }

    /// <summary>检查点行 — profile 值 + 来源文件（null = 文件缺失或解析失败，覆盖链断点）。</summary>
    private static void AppendProfileLine(StringBuilder builder, string label, string? profile, string path) =>
        builder.AppendLine($"│   {label} current.profile = {profile ?? "（未设置/文件不存在）"} ← {path}");

    /// <summary>从异常消息提取供应商名（"Provider 'xxx' …" 模式；无匹配返回 null → 探测降级）。</summary>
    internal static string? ExtractVendor(string message) =>
        System.Text.RegularExpressions.Regex.Match(message, "Provider '([^']+)'") is { Success: true } m ? m.Groups[1].Value : null;

    /// <summary>探测（纯函数 — 注入两级 settings JSON / auth JSON / env 值，测试友好；不碰文件系统）。</summary>
    /// <param name="vendor">目标供应商（auth 键与 env 变量名探测对象）。</param>
    /// <param name="userSettingsJson">用户级 settings.json 文本（null = 缺失）。</param>
    /// <param name="projectSettingsJson">项目级 settings.json 文本（null = 缺失）。</param>
    /// <param name="authJson">auth.json 文本（null = 缺失）。</param>
    /// <param name="envValue">供应商环境变量值（null = 未设置）。</param>
    internal static ProbeResult Probe(string vendor, string? userSettingsJson, string? projectSettingsJson, string? authJson, string? envValue) =>
        new(
            UserProfile: ReadProfile(userSettingsJson),
            ProjectProfile: ReadProfile(projectSettingsJson),
            AuthVendor: vendor,
            AuthKeyLength: ReadAuthKeyLength(authJson, vendor),
            EnvVarName: EnvVarNameFor(vendor),
            EnvVarValue: envValue is { Length: > 0 } ? envValue : null);

    /// <summary>探测（真实读取 — Main 消费；文件缺失/解析失败静默降级 null/0，探测永不抛）。</summary>
    internal static ProbeResult ProbeLive(string vendor) =>
        Probe(
            vendor,
            TryReadFile(UserSettingsPath),
            TryReadFile(ProjectSettingsPath),
            TryReadFile(AuthPath),
            ProbeLiveEnv(vendor));

    /// <summary>读环境变量（供应商专属名）。</summary>
    private static string? ProbeLiveEnv(string vendor) =>
        EnvVarNameFor(vendor) is { Length: > 0 } name ? Environment.GetEnvironmentVariable(name) : null;

    /// <summary>JSON 文本 → current.profile（缺失/解析失败 null — RelaxedJson 宽容 + System.Text.Json 回退）。</summary>
    private static string? ReadProfile(string? settingsJson)
    {
        if (settingsJson is null or { Length: 0 })
        {
            return null;
        }

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(settingsJson);
            return doc.RootElement.TryGetProperty("current", out var current)
                && current.TryGetProperty("profile", out var profile)
                && profile.ValueKind == System.Text.Json.JsonValueKind.String
                ? profile.GetString()
                : null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    /// <summary>auth.json → 供应商标键长度（缺失/无键/非字符串 0）。</summary>
    private static int ReadAuthKeyLength(string? authJson, string vendor)
    {
        if (authJson is null or { Length: 0 })
        {
            return 0;
        }

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(authJson);
            return doc.RootElement.TryGetProperty(vendor, out var value)
                && value.ValueKind == System.Text.Json.JsonValueKind.String
                ? value.GetString() is { Length: > 0 } key ? key.Length : 0
                : 0;
        }
        catch (System.Text.Json.JsonException)
        {
            return 0;
        }
    }

    /// <summary>供应商 → 环境变量名（ProviderEnvVar [EnumValue] 唯一数据源；未知供应商 null）。</summary>
    private static string? EnvVarNameFor(string vendor) => vendor.ToLowerInvariant() switch {
        "openai" => ProviderEnvVar.OpenAiApiKey.ToValue(),
        "azure" => ProviderEnvVar.AzureOpenAiApiKey.ToValue(),
        "anthropic" => ProviderEnvVar.AnthropicApiKey.ToValue(),
        "agnes" => ProviderEnvVar.AgnesApiKey.ToValue(),
        "deepseek" => ProviderEnvVar.DeepSeekApiKey.ToValue(),
        "sensenova" => ProviderEnvVar.SenseNovaApiKey.ToValue(),
        "zhipu" => ProviderEnvVar.ZhipuApiKey.ToValue(),
        "jev" => ProviderEnvVar.JevApiKey.ToValue(),
        _ => null
    };

    /// <summary>读文件（缺失/异常 null — 探测容错）。</summary>
    private static string? TryReadFile(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (IOException)
        {
            return null;
        }
    }
}
