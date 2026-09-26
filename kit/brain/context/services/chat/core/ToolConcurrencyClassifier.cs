namespace Core.Context;

/// <summary>
/// 工具并发安全分类器 — 对齐 TS StreamingToolExecutor.isConcurrencySafe
/// 判断工具是否可与其他工具并发执行：
/// - 在 SafeToolNames 集合中的工具可并行（由源码生成器从 [McpTool(ConcurrencySafe = true)] 生成）
/// - 不在集合中的工具默认非并发安全
/// - Bash/PowerShell 特殊处理：只读命令并发安全，写命令不安全
/// </summary>
public interface IToolConcurrencyClassifier {
    /// <summary>
    /// 判断工具调用是否并发安全
    /// </summary>
    /// <param name="toolName">工具名称</param>
    /// <param name="arguments">工具参数（用于判断 Bash 是否只读等）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>true 表示可与其他并发安全工具并行执行</returns>
    Task<bool> IsConcurrencySafeAsync(string toolName, Dictionary<string, JsonElement>? arguments, CancellationToken ct = default);
}

/// <summary>
/// 工具并发安全分类器实现 — 对齐 TS 各工具的 isConcurrencySafe() 方法
/// 静态分类由源码生成器生成 SafeToolNames（通过 DI 注入），动态分类（Bash 只读判断）由委托注入
/// </summary>
[Register(typeof(IToolConcurrencyClassifier), ServiceLifetime.Singleton)]
public sealed partial class ToolConcurrencyClassifier : ServiceEntity, IToolConcurrencyClassifier {
    private readonly FrozenSet<string> _safeToolNames;
    private readonly Func<string, bool>? _isCommandReadOnly;
    private readonly ILogger<ToolConcurrencyClassifier>? _logger;

    /// <summary>
    /// 用户交互工具名集合 — 这些工具禁止并发安全，若被错误加入白名单则记录警告
    /// </summary>
    private static readonly FrozenSet<string> UserInteractionToolNames = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        UserInteractionToolNameEnumConstants.AskUserQuestion,
        UserInteractionToolNameEnumConstants.ConfirmAction);

    /// <summary>
    /// 初始化并发安全分类器
    /// </summary>
    /// <param name="safeToolNames">并发安全工具名称集合（由源码生成器生成，通过 DI 注入）</param>
    /// <param name="isCommandReadOnly">命令只读判断委托（可选，由上层注入 IReadOnlyCommandDetector.IsReadOnly）</param>
    /// <param name="logger">日志</param>
    public ToolConcurrencyClassifier(
        FrozenSet<string>? safeToolNames = null,
        Func<string, bool>? isCommandReadOnly = null,
        ILogger<ToolConcurrencyClassifier>? logger = null) {
        _safeToolNames = safeToolNames ?? FrozenSet<string>.Empty;
        _isCommandReadOnly = isCommandReadOnly;
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task<bool> IsConcurrencySafeAsync(string toolName, Dictionary<string, JsonElement>? arguments, CancellationToken ct = default) {
        if (_safeToolNames.Contains(toolName)) {
            WarnIfInteractionToolMarkedSafe(toolName);
            return Task.FromResult(true);
        }

        if (IsBashLikeTool(toolName) && IsBashReadOnly(arguments))
            return Task.FromResult(true);

        return Task.FromResult(false);
    }

    /// <summary>
    /// 防御性检查 — 若用户交互工具被错误标记为并发安全，记录警告日志。
    /// 交互工具并发执行会绕过 StreamingToolExecutor 的独占调度，导致多个 ask 并行卡死。
    /// 此检查是双保险：即使有人错误标记 [McpTool(ConcurrencySafe=true)]，运行时也能发现。
    /// </summary>
    private void WarnIfInteractionToolMarkedSafe(string toolName) {
        if (UserInteractionToolNames.Contains(toolName)) {
            _logger?.LogWarning(
                "[ToolConcurrencyClassifier] 交互工具 '{ToolName}' 被标记为并发安全，这会绕过 StreamingToolExecutor 独占调度导致并行卡死。请移除其 ConcurrencySafe=true 标记。",
                toolName);
        }
    }

    /// <summary>
    /// 判断是否为 Bash 类工具 — 对齐 TS BashTool
    /// </summary>
    private static bool IsBashLikeTool(string toolName) {
        return string.Equals(toolName, ShellToolNameEnumConstants.Bash, StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolName, ShellToolNameEnumConstants.Powershell, StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolName, ShellToolNameEnumConstants.PowershellScript, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 判断 Bash 命令是否只读 — 对齐 TS BashTool.isConcurrencySafe(input) { return this.isReadOnly?.(input) ?? false }
    /// </summary>
    private bool IsBashReadOnly(Dictionary<string, JsonElement>? arguments) {
        if (_isCommandReadOnly is null)
            return false;

        if (arguments is null || !arguments.TryGetValue("command", out var cmdEl) || cmdEl.ValueKind != JsonValueKind.String)
            return false;

        var command = cmdEl.GetString()!;
        try {
            return _isCommandReadOnly(command);
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "[ToolConcurrencyClassifier] Bash 只读检测失败，默认非并发安全");
            return false;
        }
    }
}