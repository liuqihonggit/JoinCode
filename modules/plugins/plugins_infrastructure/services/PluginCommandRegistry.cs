namespace Core.Plugins;

/// <summary>
/// 插件命令注册表接口 — 管理插件注册的命令，支持注册、注销和查询
/// </summary>
public interface IPluginCommandRegistry {
    /// <summary>注册命令 — 返回撤销函数(可逆效应)</summary>
    Task<Action> RegisterCommandAsync(PluginCommandDefinition command, CancellationToken ct = default);
    /// <summary>注销指定名称的命令及其别名</summary>
    Task UnregisterCommandAsync(string commandName, CancellationToken ct = default);
    /// <summary>获取全部已注册命令的定义</summary>
    IEnumerable<PluginCommandDefinition> GetRegisteredCommands();
    /// <summary>按名称查询命令定义，不存在返回 null</summary>
    PluginCommandDefinition? GetCommand(string commandName);
}

/// <summary>
/// 插件命令定义 — 描述插件提供的命令名称、处理器类型、参数和别名
/// </summary>
public sealed partial class PluginCommandDefinition {
    /// <summary>命令名称</summary>
    public required string CommandName { get; init; }
    /// <summary>提供该命令的插件名称</summary>
    public required string PluginName { get; init; }
    /// <summary>命令描述</summary>
    public required string Description { get; init; }
    /// <summary>命令处理器类型全名</summary>
    public required string HandlerType { get; init; }
    /// <summary>命令参数字典（参数名 → JSON 值）</summary>
    public Dictionary<string, JsonElement> Parameters { get; init; } = [];
    /// <summary>命令别名列表</summary>
    public List<string> Aliases { get; init; } = [];
}

/// <summary>
/// 插件命令注册表 — 基于 MapRegistry 实现，支持命令注册、注销、别名和遥测
/// </summary>
[Register(typeof(MapRegistry<string, PluginCommandDefinition>), ServiceLifetime.Singleton)]
[Register(typeof(IPluginCommandRegistry), ServiceLifetime.Singleton)]
public sealed partial class PluginCommandRegistry : MapRegistry<string, PluginCommandDefinition>, IPluginCommandRegistry {
    private readonly ILogger<PluginCommandRegistry>? _logger;
    private readonly ITelemetryService? _telemetryService;

    /// <summary>
    /// 构造插件命令注册表
    /// </summary>
    /// <param name="logger">可选日志器</param>
    /// <param name="telemetryService">可选遥测服务</param>
    public PluginCommandRegistry(ILogger<PluginCommandRegistry>? logger = null, ITelemetryService? telemetryService = null)
        : base(StringComparer.OrdinalIgnoreCase) {
        _logger = logger;
        _telemetryService = telemetryService;
    }

    /// <summary>
    /// 注册命令 — 若命令已存在则跳过，同时注册所有别名；返回撤销函数
    /// </summary>
    /// <param name="command">命令定义</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>撤销函数，调用后移除该命令及其别名</returns>
    public async Task<Action> RegisterCommandAsync(PluginCommandDefinition command, CancellationToken ct = default) {
        ArgumentNullException.ThrowIfNull(command);

        if (!ContainsKey(command.CommandName)) {
            AddOrUpdateCore(command.CommandName, command);
            RecordCommandRegistryMetrics("register", command.CommandName, true);
            _logger?.LogInformation(
                "[PluginCommandRegistry] 注册命令: {Command} (插件: {Plugin}, 类型: {HandlerType})",
                command.CommandName, command.PluginName, command.HandlerType);

            foreach (var aliasDef in ExpandAliases(command)) {
                AddCore(aliasDef.CommandName, aliasDef);
            }
        } else {
            RecordCommandRegistryMetrics("register", command.CommandName, false);
            _logger?.LogWarning("[PluginCommandRegistry] 命令 '{Command}' 已存在，跳过注册", command.CommandName);
        }

        await Task.CompletedTask.ConfigureAwait(false);

        var registeredName = command.CommandName;
        var registeredAliases = command.Aliases;
        return () => {
            if (RemoveCore(registeredName, out var cmd)) {
                if (registeredAliases is { Count: > 0 }) {
                    foreach (var alias in registeredAliases)
                        RemoveCore(alias);
                }
            }
        };
    }

    /// <summary>
    /// 展开命令别名为独立的命令定义 — 每个别名生成一个继承主命令 PluginName/Description/HandlerType/Parameters 的定义
    /// <para>纯计算:不修改注册表,仅产出别名定义序列,可独立单测(不依赖时序/IO)</para>
    /// </summary>
    /// <param name="command">主命令定义</param>
    /// <returns>别名命令定义序列(无别名或空别名列表则返回空序列)</returns>
    internal static IEnumerable<PluginCommandDefinition> ExpandAliases(PluginCommandDefinition command) {
        ArgumentNullException.ThrowIfNull(command);
        if (command.Aliases is null or { Count: 0 }) yield break;
        foreach (var alias in command.Aliases) {
            yield return new PluginCommandDefinition {
                CommandName = alias,
                PluginName = command.PluginName,
                Description = command.Description,
                HandlerType = command.HandlerType,
                Parameters = command.Parameters
            };
        }
    }

    /// <summary>
    /// 注销指定名称的命令及其别名
    /// </summary>
    /// <param name="commandName">命令名称</param>
    /// <param name="ct">取消令牌</param>
    public async Task UnregisterCommandAsync(string commandName, CancellationToken ct = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandName);

        if (RemoveCore(commandName, out var command)) {
            RecordCommandRegistryMetrics("unregister", commandName, true);
            _logger?.LogInformation("[PluginCommandRegistry] 注销命令: {Command} (插件: {Plugin})",
                commandName, command.PluginName);

            if (command.Aliases is { Count: > 0 }) {
                foreach (var alias in command.Aliases) {
                    RemoveCore(alias);
                }
            }
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <summary>获取全部已注册命令的定义</summary>
    public IEnumerable<PluginCommandDefinition> GetRegisteredCommands() => GetAll();

    /// <summary>按名称查询命令定义，不存在返回 null</summary>
    public PluginCommandDefinition? GetCommand(string commandName) {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandName);
        return Get(commandName);
    }

    private void RecordCommandRegistryMetrics(string operation, string commandName, bool isSuccess) =>
        _telemetryService?.RecordCount("plugin.command.count", new Dictionary<string, string> { ["operation"] = operation, ["command"] = commandName, ["success"] = isSuccess.ToString() }, "count", "Plugin command operation count");
}