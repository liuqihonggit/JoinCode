namespace JoinCode.Abstractions.Security.Permission;

/// <summary>
/// 权限规则解析结果（TASK070 P1-P2/DSG046 — settings.json permissions 规则串的唯一数据源）。
/// </summary>
/// <param name="ToolName">工具名（"Bash"）。</param>
/// <param name="Content">内容粒度（"npm run" / "domain:example.com"）；null = 工具级规则。</param>
/// <param name="IsPrefix">是否前缀规则（"Bash(npm run:*)" — Content 为命令前缀，匹配任意以其开头的命令）。</param>
public sealed record PermissionRuleValue(string ToolName, string? Content, bool IsPrefix);

/// <summary>
/// 权限规则串解析器（TASK070 P1-P2/DSG046 — Parse/Format 双向同一实现，禁止双向维护）：
/// 规则串三形态（对齐 TS permissionRuleParser.ts:93-133）：
/// "Bash" — 工具级；"WebFetch(domain:example.com)" — 内容精确；"Bash(npm run:*)" — 命令前缀。
/// 消费方：ServiceRegistration 启动加载、PermissionChecker 持久化、ToolListPermissionMiddleware 判定、
/// PermissionAwareToolExecutor AlwaysAllow 规则生成。
/// </summary>
public static class PermissionRuleParser {
    /// <summary>前缀规则后缀（"Bash(npm run:*)" — 对齐 TS 前缀粒度语义）。</summary>
    private const string PrefixSuffix = ":*";

    /// <summary>
    /// 解析规则串 — 非法（空/无工具名）返回 null。
    /// </summary>
    /// <param name="ruleValue">规则串（"Bash" / "Bash(npm run:*)" / "WebFetch(domain:example.com)"）。</param>
    /// <returns>解析结果；null = 非法规则串。</returns>
    public static PermissionRuleValue? Parse(string? ruleValue) {
        if (string.IsNullOrWhiteSpace(ruleValue)) {
            return null;
        }

        var parenIndex = ruleValue.IndexOf('(');
        if (parenIndex == 0) {
            return null; // "(noTool)" 形态 — 有内容括号但无工具名，非法
        }

        if (parenIndex > 0 && ruleValue.EndsWith(')')) {
            var toolName = ruleValue[..parenIndex].Trim();
            if (toolName.Length == 0) {
                return null;
            }

            var content = ruleValue.Substring(parenIndex + 1, ruleValue.Length - parenIndex - 2);
            var isPrefix = content.EndsWith(PrefixSuffix, StringComparison.Ordinal);
            if (isPrefix) {
                content = content[..^PrefixSuffix.Length];
            }

            return new PermissionRuleValue(toolName, content.Length > 0 ? content : null, isPrefix);
        }

        return new PermissionRuleValue(ruleValue.Trim(), null, false);
    }

    /// <summary>
    /// 格式化规则串 — Parse 的逆运算（同一实现保证往返一致）。
    /// </summary>
    /// <param name="toolName">工具名。</param>
    /// <param name="content">内容粒度（null = 工具级规则）。</param>
    /// <param name="isPrefix">前缀规则（content 尾部追加 ":*"）。</param>
    /// <returns>规则串。</returns>
    public static string Format(string toolName, string? content = null, bool isPrefix = false) {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        if (content is null) {
            return toolName;
        }

        return isPrefix ? $"{toolName}({content}{PrefixSuffix})" : $"{toolName}({content})";
    }

    /// <summary>
    /// 内容粒度匹配（纯函数）：前缀规则 — 命令以 Content 开头；精确规则 — 命令完全相等。
    /// </summary>
    /// <param name="ruleContent">规则内容（"npm run" 前缀或 "domain:example.com" 精确）。</param>
    /// <param name="isPrefix">是否前缀规则。</param>
    /// <param name="command">实际命令串（如 Bash 的 command 参数值）。</param>
    /// <returns>命中 true。</returns>
    public static bool MatchesContent(string ruleContent, bool isPrefix, string? command) {
        if (string.IsNullOrEmpty(command)) {
            return false;
        }

        return isPrefix
            ? command.StartsWith(ruleContent, StringComparison.Ordinal)
            : string.Equals(ruleContent, command, StringComparison.Ordinal);
    }

    /// <summary>
    /// 从工具调用参数提取命令串 — 识别 Shell 类 command 字段（Bash/PowerShell 等）；
    /// 非 Shell 工具或缺失字段返回 null（内容粒度匹配跳过，落既有判定）。
    /// </summary>
    /// <param name="arguments">工具调用参数。</param>
    /// <returns>命令串；null = 无可匹配命令。</returns>
    public static string? ExtractCommand(IReadOnlyDictionary<string, JsonElement>? arguments) {
        if (arguments is null || arguments.Count == 0) {
            return null;
        }

        if (arguments.TryGetValue("command", out var commandElement) && commandElement.ValueKind == JsonValueKind.String) {
            var command = commandElement.GetString();
            return string.IsNullOrWhiteSpace(command) ? null : command.Trim();
        }

        return null;
    }
}
