namespace McpToolDispatch;

/// <summary>
/// GitHub Auth/Config 工具 — auth status 检查 API 客户端配置，config get/set 简化提示
/// <para>auth login/refresh/token 涉及 OAuth 流程，需用系统 gh CLI；config 读取 gh config.yml</para>
/// </summary>
public partial class GitHubToolHandlers {
    /// <summary>
    /// 查看 Auth 状态 — 检查 API 客户端配置状态，调 GET /user 验证 token 有效性
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhAuthStatus, "查看 GitHub Auth 状态", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhAuthStatusAsync(
        CancellationToken cancellationToken = default) {
        if (_apiClient is null) return Fail("GitHub REST API 客户端未配置(IGitHubApiClient 未注入) — auth login 需用系统 gh CLI: gh auth login");
        var result = await _apiClient.SendAsync(HttpMethod.Get, "user", ct: cancellationToken).ConfigureAwait(false);
        if (!result.Success) return Fail($"Auth 验证失败: {result.Error} — 请用系统 gh CLI 重新登录: gh auth login");
        try {
            var user = JsonSerializer.Deserialize(result.Body, GitHubApiJsonContext.Safe.AuthUserResponse);
            var login = user?.Login ?? "";
            var name = user?.Name ?? "";
            var sb = new StringBuilder(128);
            sb.AppendLine($"已登录: {login}");
            if (!string.IsNullOrEmpty(name)) sb.AppendLine($"名称: {name}");
            sb.Append("Token 状态: 有效");
            return Ok(sb.ToString());
        } catch { return Ok("Auth 状态: 已配置(响应解析失败)"); }
    }

    /// <summary>
    /// 登录 GitHub — 接受 PAT token，调 GET /user 验证后保存到 hosts.yml
    /// <para>OAuth 设备流程需调 github.com（非 api.github.com），MCP 工具无 HttpClient，故用 PAT 方式</para>
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhAuthLogin, "登录 GitHub(PAT token 验证后保存到 hosts.yml)", "github")]
    public async Task<ToolResult> GhAuthLoginAsync(
        [McpToolParameter("GitHub PAT token(ghp_xxx)", Required = true)] string token,
        [McpToolParameter("host(可选,默认 github.com)", Required = false)] string? host = null,
        CancellationToken cancellationToken = default) {
        if (_apiClient is null) return Fail("GitHub REST API 客户端未配置(IGitHubApiClient 未注入) — 无法验证 token");
        var result = await _apiClient.SendAsync(HttpMethod.Get, "user", ct: cancellationToken).ConfigureAwait(false);
        if (!result.Success) return Fail($"Token 验证失败: {result.Error} — 请检查 token 是否有效");
        string login;
        try {
            var user = JsonSerializer.Deserialize(result.Body, GitHubApiJsonContext.Safe.AuthUserResponse);
            login = user?.Login ?? "";
        } catch { return Fail("Token 验证响应解析失败"); }
        var hostname = string.IsNullOrWhiteSpace(host) ? "github.com" : host;
        var hostsPath = GetGhConfigPath(true);
        var content = _fs.FileExists(hostsPath) ? await _fs.ReadAllTextAsync(hostsPath, cancellationToken).ConfigureAwait(false) : "";
        var updated = SetYamlHostToken(content, hostname, login, token);
        await _fs.WriteAllTextAsync(hostsPath, updated, cancellationToken).ConfigureAwait(false);
        return Ok($"已登录: {login} (host: {hostname})\nToken 已保存到 {hostsPath}");
    }

    /// <summary>
    /// 获取 Auth token — 从 hosts.yml 读取 oauth_token
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhAuthToken, "获取 GitHub Auth token(从 hosts.yml 读取)", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhAuthTokenAsync(
        [McpToolParameter("host(可选,默认 github.com)", Required = false)] string? host = null,
        CancellationToken cancellationToken = default) {
        var hostname = string.IsNullOrWhiteSpace(host) ? "github.com" : host;
        var hostsPath = GetGhConfigPath(true);
        if (!_fs.FileExists(hostsPath)) return Fail($"hosts.yml 不存在: {hostsPath} — 请先登录: gh auth login");
        var content = await _fs.ReadAllTextAsync(hostsPath, cancellationToken).ConfigureAwait(false);
        var token = TryGetYamlValue(content, "oauth_token", hostname);
        return token is not null ? Ok(token) : Fail($"未找到 {hostname} 的 oauth_token — 请先登录: gh auth login");
    }

    /// <summary>
    /// 刷新 Auth token — 需用系统 gh CLI（OAuth refresh_token 流程）
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhAuthRefresh, "刷新 GitHub Auth token(提示用系统 gh)", "github")]
    public Task<ToolResult> GhAuthRefreshAsync(
        [McpToolParameter("host(可选,默认 github.com)", Required = false)] string? host = null,
        CancellationToken cancellationToken = default)
        => Task.FromResult(Fail("auth refresh 需 OAuth refresh_token 流程，jcc 未实现。请用系统 gh CLI: gh auth refresh" + (string.IsNullOrWhiteSpace(host) ? "" : $" --host {host}")));

    /// <summary>
    /// 设置 hosts.yml 中 host 下的 user 和 oauth_token（不存在则追加 host section）
    /// </summary>
    private static string SetYamlHostToken(string content, string hostname, string user, string token) {
        var lines = new List<string>(content.Split('\n'));
        var hostLineIndex = lines.FindIndex(l => l.TrimStart().StartsWith($"{hostname}:", StringComparison.OrdinalIgnoreCase));
        if (hostLineIndex < 0) {
            lines.Add($"{hostname}:");
            lines.Add($"    user: {user}");
            lines.Add($"    oauth_token: {token}");
            lines.Add("    git_protocol: https");
            return string.Join("\n", lines);
        }
        var hostIndent = lines[hostLineIndex].Length - lines[hostLineIndex].TrimStart().Length;
        var entryIndent = hostIndent + 4;
        var userSet = false;
        var tokenSet = false;
        for (var i = hostLineIndex + 1; i < lines.Count; i++) {
            var trimmed = lines[i].TrimStart();
            var indent = lines[i].Length - trimmed.Length;
            if (trimmed.Length == 0) continue;
            if (indent <= hostIndent) break;
            if (trimmed.StartsWith("user:", StringComparison.OrdinalIgnoreCase)) { lines[i] = new string(' ', indent) + $"user: {user}"; userSet = true; }
            else if (trimmed.StartsWith("oauth_token:", StringComparison.OrdinalIgnoreCase)) { lines[i] = new string(' ', indent) + $"oauth_token: {token}"; tokenSet = true; }
        }
        if (!userSet) lines.Insert(hostLineIndex + 1, new string(' ', entryIndent) + $"user: {user}");
        if (!tokenSet) lines.Insert(hostLineIndex + 2, new string(' ', entryIndent) + $"oauth_token: {token}");
        return string.Join("\n", lines);
    }

    /// <summary>
    /// 读取配置 — 读写 gh config.yml 顶层 key-value（host 参数读写 hosts.yml per-host 设置）
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhConfigGet, "读取 gh 配置(读写 config.yml)", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhConfigGetAsync(
        [McpToolParameter("配置键名(如 git_protocol/editor/prompt)", Required = true)] string key,
        [McpToolParameter("host(可选,per-host 设置,读写 hosts.yml)", Required = false)] string? host = null,
        CancellationToken cancellationToken = default) {
        var configPath = GetGhConfigPath(host is not null);
        if (!_fs.FileExists(configPath)) return Fail($"gh 配置文件不存在: {configPath} — 请用系统 gh CLI 登录: gh auth login");
        var content = await _fs.ReadAllTextAsync(configPath, cancellationToken).ConfigureAwait(false);
        var value = TryGetYamlValue(content, key, host);
        return value is not null ? Ok(value) : Fail($"配置键不存在: {key}{(string.IsNullOrWhiteSpace(host) ? "" : $" (host: {host})")}");
    }

    /// <summary>
    /// 写入配置 — 读写 gh config.yml 顶层 key-value（host 参数读写 hosts.yml per-host 设置）
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhConfigSet, "写入 gh 配置(读写 config.yml)", "github")]
    public async Task<ToolResult> GhConfigSetAsync(
        [McpToolParameter("配置键名", Required = true)] string key,
        [McpToolParameter("配置值", Required = true)] string value,
        [McpToolParameter("host(可选,per-host 设置,读写 hosts.yml)", Required = false)] string? host = null,
        CancellationToken cancellationToken = default) {
        var configPath = GetGhConfigPath(host is not null);
        if (!_fs.FileExists(configPath)) return Fail($"gh 配置文件不存在: {configPath} — 请用系统 gh CLI 登录: gh auth login");
        var content = await _fs.ReadAllTextAsync(configPath, cancellationToken).ConfigureAwait(false);
        var updated = SetYamlValue(content, key, value, host);
        await _fs.WriteAllTextAsync(configPath, updated, cancellationToken).ConfigureAwait(false);
        return Ok($"已设置 {key} = {value}");
    }

    /// <summary>
    /// 获取 gh 配置文件路径 — config.yml 或 hosts.yml
    /// </summary>
    /// <param name="hostsFile">true 返回 hosts.yml 路径,false 返回 config.yml 路径</param>
    internal static string GetGhConfigPath(bool hostsFile) {
        var fileName = hostsFile ? "hosts.yml" : "config.yml";
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrEmpty(appData) && OperatingSystem.IsWindows()) return Path.Combine(appData, "GitHub CLI", fileName);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".config", "gh", fileName);
    }

    /// <summary>
    /// 从 YAML 内容中读取 key 对应的 value — 简化行解析，支持 host 嵌套
    /// </summary>
    private static string? TryGetYamlValue(string content, string key, string? host) {
        var lines = content.Split('\n');
        if (string.IsNullOrWhiteSpace(host)) return FindTopLevelValue(lines, key);
        var hostIndent = -1;
        for (var i = 0; i < lines.Length; i++) {
            var trimmed = lines[i].TrimStart();
            if (trimmed.StartsWith($"{host}:", StringComparison.OrdinalIgnoreCase)) { hostIndent = lines[i].Length - trimmed.Length; break; }
        }
        if (hostIndent < 0) return null;
        for (var i = 0; i < lines.Length; i++) {
            var trimmed = lines[i].TrimStart();
            var indent = lines[i].Length - trimmed.Length;
            if (indent <= hostIndent && i > 0 && lines[i - 1].TrimStart().StartsWith($"{host}:", StringComparison.OrdinalIgnoreCase)) break;
            if (indent > hostIndent && trimmed.StartsWith($"{key}:", StringComparison.OrdinalIgnoreCase))
                return trimmed[(key.Length + 1)..].Trim();
        }
        return null;
    }

    /// <summary>
    /// 查找 YAML 顶层 key-value
    /// </summary>
    private static string? FindTopLevelValue(string[] lines, string key) {
        foreach (var line in lines) {
            if (line.StartsWith('#')) continue;
            if (line.Length > 0 && char.IsWhiteSpace(line[0])) continue;
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith($"{key}:", StringComparison.OrdinalIgnoreCase))
                return trimmed[(key.Length + 1)..].Trim();
        }
        return null;
    }

    /// <summary>
    /// 设置 YAML 顶层 key-value（不存在则追加），支持 host 嵌套
    /// </summary>
    private static string SetYamlValue(string content, string key, string value, string? host) {
        var lines = new List<string>(content.Split('\n'));
        if (string.IsNullOrWhiteSpace(host)) return ReplaceTopLevelValue(lines, key, value);
        var hostLineIndex = lines.FindIndex(l => l.TrimStart().StartsWith($"{host}:", StringComparison.OrdinalIgnoreCase));
        if (hostLineIndex < 0) {
            lines.Add($"    {host}:");
            lines.Add($"        {key}: {value}");
            return string.Join("\n", lines);
        }
        var hostIndent = lines[hostLineIndex].Length - lines[hostLineIndex].TrimStart().Length;
        for (var i = hostLineIndex + 1; i < lines.Count; i++) {
            var trimmed = lines[i].TrimStart();
            var indent = lines[i].Length - trimmed.Length;
            if (indent <= hostIndent) break;
            if (indent > hostIndent && trimmed.StartsWith($"{key}:", StringComparison.OrdinalIgnoreCase)) {
                lines[i] = new string(' ', indent) + $"{key}: {value}";
                return string.Join("\n", lines);
            }
        }
        lines.Insert(hostLineIndex + 1, new string(' ', hostIndent + 4) + $"{key}: {value}");
        return string.Join("\n", lines);
    }

    /// <summary>
    /// 替换 YAML 顶层 key-value（不存在则追加）
    /// </summary>
    private static string ReplaceTopLevelValue(List<string> lines, string key, string value) {
        for (var i = 0; i < lines.Count; i++) {
            if (lines[i].StartsWith('#')) continue;
            if (lines[i].Length > 0 && char.IsWhiteSpace(lines[i][0])) continue;
            var trimmed = lines[i].TrimStart();
            if (trimmed.StartsWith($"{key}:", StringComparison.OrdinalIgnoreCase)) {
                lines[i] = $"{key}: {value}";
                return string.Join("\n", lines);
            }
        }
        lines.Add($"{key}: {value}");
        return string.Join("\n", lines);
    }

    /// <summary>
    /// 解析 YAML 中某个 section 下的所有 key-value（如 aliases: 下的条目）
    /// </summary>
    internal static List<(string Key, string Value)> ParseYamlSection(string content, string section) {
        var result = new List<(string, string)>();
        var lines = content.Split('\n');
        var sectionIndent = -1;
        for (var i = 0; i < lines.Length; i++) {
            var trimmed = lines[i].TrimStart();
            if (trimmed.StartsWith($"{section}:", StringComparison.OrdinalIgnoreCase)) {
                sectionIndent = lines[i].Length - trimmed.Length;
                for (var j = i + 1; j < lines.Length; j++) {
                    var innerTrimmed = lines[j].TrimStart();
                    var innerIndent = lines[j].Length - innerTrimmed.Length;
                    if (innerTrimmed.Length == 0) continue;
                    if (innerIndent <= sectionIndent) break;
                    var colonIdx = innerTrimmed.IndexOf(':');
                    if (colonIdx <= 0) continue;
                    var key = innerTrimmed[..colonIdx].Trim();
                    var value = innerTrimmed[(colonIdx + 1)..].Trim();
                    result.Add((key, value));
                }
                break;
            }
        }
        return result;
    }

    /// <summary>
    /// 设置 YAML section 下的 key-value（不存在则追加）
    /// </summary>
    internal static string SetYamlSectionValue(string content, string section, string key, string value) {
        var lines = new List<string>(content.Split('\n'));
        var sectionLineIndex = -1;
        var sectionIndent = 0;
        for (var i = 0; i < lines.Count; i++) {
            var trimmed = lines[i].TrimStart();
            if (trimmed.StartsWith($"{section}:", StringComparison.OrdinalIgnoreCase)) {
                sectionLineIndex = i;
                sectionIndent = lines[i].Length - trimmed.Length;
                break;
            }
        }
        if (sectionLineIndex < 0) {
            lines.Add($"{section}:");
            lines.Add($"    {key}: {value}");
            return string.Join("\n", lines);
        }
        var entryIndent = sectionIndent + 4;
        for (var i = sectionLineIndex + 1; i < lines.Count; i++) {
            var trimmed = lines[i].TrimStart();
            var indent = lines[i].Length - trimmed.Length;
            if (trimmed.Length == 0) continue;
            if (indent <= sectionIndent) break;
            if (trimmed.StartsWith($"{key}:", StringComparison.OrdinalIgnoreCase)) {
                lines[i] = new string(' ', indent) + $"{key}: {value}";
                return string.Join("\n", lines);
            }
        }
        lines.Insert(sectionLineIndex + 1, new string(' ', entryIndent) + $"{key}: {value}");
        return string.Join("\n", lines);
    }

    /// <summary>
    /// 删除 YAML section 下的 key（不存在返回原内容）
    /// </summary>
    internal static string DeleteYamlSectionValue(string content, string section, string key) {
        var lines = new List<string>(content.Split('\n'));
        var sectionIndent = -1;
        var deleteIndex = -1;
        for (var i = 0; i < lines.Count; i++) {
            var trimmed = lines[i].TrimStart();
            if (trimmed.StartsWith($"{section}:", StringComparison.OrdinalIgnoreCase)) {
                sectionIndent = lines[i].Length - trimmed.Length;
                for (var j = i + 1; j < lines.Count; j++) {
                    var innerTrimmed = lines[j].TrimStart();
                    var innerIndent = lines[j].Length - innerTrimmed.Length;
                    if (innerTrimmed.Length == 0) continue;
                    if (innerIndent <= sectionIndent) break;
                    if (innerTrimmed.StartsWith($"{key}:", StringComparison.OrdinalIgnoreCase)) { deleteIndex = j; break; }
                }
                break;
            }
        }
        if (deleteIndex < 0) return content;
        lines.RemoveAt(deleteIndex);
        return string.Join("\n", lines);
    }
}
