namespace Core.Query;

/// <summary>
/// 只读命令探测委托 — 由组合根注入（委托 IReadOnlyCommandDetector.CheckReadOnlyConstraints），
/// 避免 brain.query 反向依赖 guard.security。
/// </summary>
/// <param name="command">原始 shell 命令文本。</param>
/// <returns>true 表示命令只读，可与其他并发安全工具并行。</returns>
public delegate bool ReadOnlyCommandProbe(string command);

/// <summary>
/// 工具并发安全分类器 — 对齐 TS StreamingToolExecutor.isConcurrencySafe（ADR 0160）。
/// 静态分类来自源码生成器 <c>SafeToolNames</c>（[McpTool(ConcurrencySafe = true)]），
/// 动态分类覆盖 Bash/PowerShell 只读命令；交互工具始终独占。
/// </summary>
public interface IToolConcurrencyClassifier {
    /// <summary>
    /// 判断工具调用是否可与其他并发安全工具并行执行。
    /// </summary>
    /// <param name="toolName">工具名称。</param>
    /// <param name="arguments">工具参数（Bash 只读探测需要 command）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>true 表示并发安全；解析不到分类器时调用方按串行处理。</returns>
    Task<bool> IsConcurrencySafeAsync(string toolName, Dictionary<string, JsonElement>? arguments, CancellationToken ct = default);
}

/// <summary>
/// 分类器默认实现 — SafeToolNames + 只读命令探测；交互工具即使误标并发安全也强制独占。
/// </summary>
[Register(typeof(IToolConcurrencyClassifier), ServiceLifetime.Singleton)]
public sealed partial class ToolConcurrencyClassifier : ServiceEntity, IToolConcurrencyClassifier {
    private static readonly FrozenSet<string> InteractionToolNames = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        UserInteractionToolNameEnumConstants.AskUserQuestion,
        UserInteractionToolNameEnumConstants.ConfirmAction);

    private readonly FrozenSet<string> _safeToolNames;
    private readonly ReadOnlyCommandProbe? _readOnlyProbe;
    private readonly ILogger<ToolConcurrencyClassifier>? _logger;

    /// <summary>
    /// 初始化分类器。
    /// </summary>
    /// <param name="safeToolNames">并发安全工具集合（DI 注入生成器产出的 SafeToolNames）。</param>
    /// <param name="readOnlyProbe">只读命令探测委托（可选）。</param>
    /// <param name="logger">日志。</param>
    public ToolConcurrencyClassifier(
        FrozenSet<string>? safeToolNames = null,
        ReadOnlyCommandProbe? readOnlyProbe = null,
        ILogger<ToolConcurrencyClassifier>? logger = null) {
        _safeToolNames = safeToolNames ?? FrozenSet<string>.Empty;
        _readOnlyProbe = readOnlyProbe;
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task<bool> IsConcurrencySafeAsync(string toolName, Dictionary<string, JsonElement>? arguments, CancellationToken ct = default) {
        if (InteractionToolNames.Contains(toolName)) {
            if (_safeToolNames.Contains(toolName)) {
                _logger?.LogWarning(
                    "[ToolConcurrencyClassifier] 交互工具 '{ToolName}' 被标记为并发安全，已强制独占调度", toolName);
            }
            return Task.FromResult(false);
        }

        if (_safeToolNames.Contains(toolName))
            return Task.FromResult(true);

        if (IsShellLikeTool(toolName) && IsReadOnlyCommand(arguments))
            return Task.FromResult(true);

        return Task.FromResult(false);
    }

    private static bool IsShellLikeTool(string toolName) {
        return string.Equals(toolName, ShellToolNameEnumConstants.Bash, StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolName, ShellToolNameEnumConstants.Powershell, StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolName, ShellToolNameEnumConstants.PowershellScript, StringComparison.OrdinalIgnoreCase);
    }

    private bool IsReadOnlyCommand(Dictionary<string, JsonElement>? arguments) {
        if (_readOnlyProbe is null)
            return false;
        if (arguments is null || !arguments.TryGetValue("command", out var commandElement) || commandElement.ValueKind != JsonValueKind.String)
            return false;
        var command = commandElement.GetString();
        if (string.IsNullOrWhiteSpace(command))
            return false;

        try {
            return _readOnlyProbe(command);
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "[ToolConcurrencyClassifier] 只读命令探测失败，按非并发安全处理");
            return false;
        }
    }
}
