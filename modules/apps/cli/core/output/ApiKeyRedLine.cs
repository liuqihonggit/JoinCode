namespace JoinCode.Cli.Output;

/// <summary>
/// 密钥红线检查器 — 对齐架构指南安全设计
/// 核心原则: 禁止在命令行参数中传递 API Key（会被 ps/top/进程列表暴露）
/// 正确做法: 使用供应商专属环境变量（如 OPENAI_API_KEY=xxx）或 auth.json
/// <para><b>判定依据是「值」，不是「参数名」</b>：参数名长得像密钥不代表它就是密钥 ——
/// <c>--key</c> / <c>--token</c> / <c>--secret</c> 是极常见的业务参数名（配置项、认证方式、
/// 密文），按名字一刀切会让这些工具完全不可用；而真正要防的是「密钥值出现在进程列表里」。
/// 所以分三层：</para>
/// <list type="number">
/// <item>参数名有<b>明确密钥语义</b>（--api-key / --access-token / --password 等）→ 拒绝，与值无关；</item>
/// <item>参数<b>值</b>命中密钥形态（sk- / ghp_ / AKIA / eyJ 等前缀）→ 拒绝，与参数名无关；</item>
/// <item>参数名是<b>通用词</b>（--key / --token / --secret）且值同时呈现不透明密钥形态
/// （足够长 + 只含 token 字符集）→ 拒绝；值是普通词（如 theme）→ 放行。</item>
/// </list>
/// <para>白名单: <c>mcp serve --token</c>（或 <c>mcp_serve --token</c>）— ADR 0138 该 token 是本地
/// MCP 服务端口令（本进程生成、本地 client 消费，非供应商 API Key），脚本集成场景需显式复用 token，故放行。</para>
/// </summary>
public static class ApiKeyRedLine {
    /// <summary>
    /// 参数名带<b>明确密钥语义</b> —— 出现即拒绝，与值无关。
    /// <para>这份名单只收「除了密钥不会有别的含义」的名字。通用的 --key / --token / --secret
    /// 不在其中：它们是业务参数名，误伤会让工具彻底不可用。</para>
    /// </summary>
    private static readonly FrozenSet<string> ExplicitSecretOptions = new[]
    {
        "--api-key", "--apikey", "--access-key", "--secret-key", "--private-key",
        "--client-secret", "--access-token", "--refresh-token", "--bearer-token",
        "--password", "--passwd", "--credential", "--credentials",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// 参数名是<b>通用词</b> —— 只有当值同时呈现不透明密钥形态时才拒绝。
    /// </summary>
    private static readonly FrozenSet<string> GenericSecretWords = new[]
    {
        "--key", "--token", "--secret", "--auth", "--credential-value",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// 密钥形态的值前缀 —— 命中即拒绝，与参数名无关。
    /// <para>覆盖各供应商的公开 key 形态；自建服务的不透明 token 走「通用词 + 值形态」那条规则。</para>
    /// </summary>
    private static readonly string[] SecretValuePrefixes = {
        "sk-", "sk_live_", "sk_test_",
        "ghp_", "gho_", "ghu_", "ghs_", "ghr_", "github_pat_",
        "glpat-", "glptt-",
        "xoxb-", "xoxp-", "xoxa-", "xoxr-", "xapp-",
        "AKIA", "ASIA",
        "eyJ",
        "AIza", "ya29.",
        "hf_", "npm_", "dop_v1_", "dop_v2_",
        "shpat_", "shpca_", "shppa_", "shpss_",
        "sq0atp-", "sq0csp-", "sq0idp-",
        "dckr_pat_", "dckr_oauth_",
        "key-", "SG.",
    };

    /// <summary>不透明密钥形态的最小长度 —— 短于此的基本不是密钥。</summary>
    private const int OpaqueSecretMinLength = 24;

    /// <summary>
    /// 检查命令行参数是否包含 API Key 模式
    /// </summary>
    /// <param name="args">命令行参数。</param>
    /// <returns>如果检测到密钥泄露风险返回错误描述，否则 null</returns>
    public static string? CheckArgsForSecrets(string[] args) {
        if (args is null || args.Length == 0)
            return null;

        // 上下文白名单: mcp serve --token / mcp_serve --token（ADR 0138）
        var lowerArgs = args.Where(static a => !a.StartsWith('-')).Select(static a => a.ToLowerInvariant()).ToArray();
        var isMcpServe = lowerArgs.Contains("mcp_serve")
            || (lowerArgs.Contains("mcp") && lowerArgs.Contains("serve"));
        if (isMcpServe)
            return null;

        for (var i = 1; i < args.Length; i++) {
            var token = args[i];
            if (!token.StartsWith("--")) continue;

            var (name, inlineValue) = SplitOption(token);
            if (ExplicitSecretOptions.Contains(name))
                return BuildMessage($"检测到命令行参数 '{token}' 可能包含 API Key。");

            var value = inlineValue ?? ReadValue(args, i);
            if (value is null) continue;
            if (HasSecretValueShape(value))
                return BuildMessage($"检测到命令行参数值 '{Mask(value)}' 可能是 API Key。");
            if (GenericSecretWords.Contains(name) && LooksLikeOpaqueSecret(value))
                return BuildMessage($"检测到命令行参数 '{name}' 的值 '{Mask(value)}' 形似密钥。");
        }

        return null;
    }

    /// <summary>拆分 <c>--名称=值</c> —— 没有 <c>=</c> 时值取 null（由下一个 token 决定）。</summary>
    private static (string Name, string? InlineValue) SplitOption(string token) {
        var eq = token.IndexOf('=');
        return eq > 2 ? (token[..eq], token[(eq + 1)..]) : (token, null);
    }

    /// <summary>读取选项的值 — 下一个 token 不以 <c>-</c> 开头时才算值。</summary>
    private static string? ReadValue(string[] args, int index) {
        if (index + 1 >= args.Length) return null;
        var next = args[index + 1];
        return next.StartsWith('-') ? null : next;
    }

    /// <summary>值是否命中已知的密钥前缀。</summary>
    private static bool HasSecretValueShape(string value) {
        foreach (var prefix in SecretValuePrefixes) {
            if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 值是否呈现「不透明密钥」形态 —— 足够长且只含 token 字符集。
    /// <para>这条专门兜住自建服务的不透明密钥：它们没有可识别的厂商前缀，
    /// 但长度与字符集与普通业务值（如配置项名、路径、URL）明显不同。</para>
    /// </summary>
    private static bool LooksLikeOpaqueSecret(string value) {
        if (value.Length < OpaqueSecretMinLength) return false;
        foreach (var c in value) {
            var allowed = char.IsLetterOrDigit(c) || c is '_' or '-' or '+' or '/' or '=' or '.';
            if (!allowed) return false;
        }
        return true;
    }

    /// <summary>错误提示 — 说明原因、触发点与正确做法。</summary>
    private static string BuildMessage(string head) =>
        head
        + "禁止在命令行中传递密钥（会被 ps/top/进程列表暴露）。"
        + "请使用供应商专属环境变量（如 OPENAI_API_KEY=xxx），或在 ~/.jcc/auth.json 中配置。";

    /// <summary>掩码值 — 只露出前几个字符，避免提示本身把密钥抄进日志。</summary>
    private static string Mask(string value) =>
        value[..Math.Min(8, value.Length)] + "...";
}
