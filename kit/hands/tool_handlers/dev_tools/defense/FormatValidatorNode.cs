namespace Tools.Handlers;

/// <summary>
/// 格式校验 node — 独立公共对象，提供 settings.json 编辑校验、keyword-sections.json 权限校验、doctor Agent 路径校验。
/// 文件编辑工具注入此 node 在编辑前校验内容合法性与权限。
/// </summary>
[Register(typeof(FormatValidatorNode), ServiceLifetime.Singleton)]
public sealed class FormatValidatorNode {
    private readonly IFileSystem _fs;
    private readonly ISubAgentContextAccessor? _subAgentContextAccessor;
    private readonly ILogger<FormatValidatorNode>? _logger;

    /// <summary>
    /// 构造格式校验 node
    /// </summary>
    /// <param name="fs">文件系统抽象</param>
    /// <param name="subAgentContextAccessor">可选的子 Agent 上下文访问器</param>
    /// <param name="logger">可选日志记录器</param>
    public FormatValidatorNode(
        IFileSystem fs,
        ISubAgentContextAccessor? subAgentContextAccessor = null,
        ILogger<FormatValidatorNode>? logger = null) {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _subAgentContextAccessor = subAgentContextAccessor;
        _logger = logger;
    }

    /// <summary>
    /// settings 文件编辑校验 — 只阻止"从合法变非法"的降级编辑，不阻止修复。
    /// 预模拟编辑：用 old_string/new_string/replace_all 计算编辑后内容，再校验合法性。
    /// 对齐 TS: FileEditTool.ts L346 — validateInputForSettingsFileEdit。
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="oldString">旧字符串</param>
    /// <param name="newString">新字符串</param>
    /// <param name="replaceAll">是否替换全部</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>null 表示校验通过，错误消息表示校验失败</returns>
    public async ValueTask<string?> ValidateSettingsEditAsync(
        string filePath, string oldString, string newString, bool replaceAll, CancellationToken ct) {
        if (!SettingsEditValidator.IsJccSettingsPath(filePath) || !_fs.FileExists(filePath))
            return null;

        var detectedEncoding = await FileEncodingDetector.DetectFromFileAsync(filePath, _fs, ct).ConfigureAwait(false);
        var originalContent = await _fs.ReadAllTextAsync(filePath, detectedEncoding, ct).ConfigureAwait(false);

        // 对齐 TS: 预模拟编辑 — 使用与 FileEditTool 相同的替换逻辑
        var updatedContent = replaceAll
            ? originalContent.Replace(oldString, newString)
            : ReplaceFirst(originalContent, oldString, newString);

        return SettingsEditValidator.ValidateEdit(filePath, originalContent, updatedContent);
    }

    /// <summary>
    /// keyword-sections.json 编辑权限校验 — 仅 keywordMaintenance Agent 可编辑。
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>null 表示允许编辑，错误消息表示权限拒绝</returns>
    public string? ValidateKeywordSectionsEdit(string filePath) {
        if (!PathGuardNode.IsKeywordSectionsPath(filePath) || !_fs.FileExists(filePath))
            return null;

        var currentAgentType = _subAgentContextAccessor?.Current?.Variant?.ToValue() ?? _subAgentContextAccessor?.Current?.Role.ToValue();
        if (currentAgentType is null || currentAgentType.Equals("keywordMaintenance", StringComparison.OrdinalIgnoreCase))
            return null;

        return "keyword-sections.json 只能由 keywordMaintenance Agent 编辑";
    }

    /// <summary>
    /// doctor Agent 编辑路径校验 — 仅允许编辑 .jcc/diag/、.jcc/reflexion/、worktree 内文件。
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>null 表示允许编辑，错误消息表示路径拒绝</returns>
    public string? ValidateDoctorAgentEdit(string filePath) {
        var agentType = _subAgentContextAccessor?.Current?.Variant?.ToValue() ?? _subAgentContextAccessor?.Current?.Role.ToValue();
        if (agentType is null || !agentType.Equals("doctor", StringComparison.OrdinalIgnoreCase))
            return null;
        if (PathGuardNode.IsDoctorAllowedEditPath(filePath))
            return null;

        return "doctor Agent 只能编辑 .jcc/diag/、.jcc/reflexion/ 和 worktree 内文件";
    }

    /// <summary>替换字符串中第一个匹配项（用于预模拟编辑）。对齐 TS: file.replace。</summary>
    private static string ReplaceFirst(string text, string search, string replace) {
        var index = text.IndexOf(search, StringComparison.Ordinal);
        if (index < 0) return text;
        return string.Concat(text.AsSpan(0, index), replace, text.AsSpan(index + search.Length));
    }
}