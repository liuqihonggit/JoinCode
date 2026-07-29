namespace Core.Plugins;

public interface IPluginCommandRegistry
{
    Task RegisterCommandAsync(PluginCommandDefinition command, CancellationToken ct = default);
    Task UnregisterCommandAsync(string commandName, CancellationToken ct = default);
    IReadOnlyList<PluginCommandDefinition> GetRegisteredCommands();
    PluginCommandDefinition? GetCommand(string commandName);
}

public sealed partial class PluginCommandDefinition
{
    public required string CommandName { get; init; }
    public required string PluginName { get; init; }
    public required string Description { get; init; }
    public required string HandlerType { get; init; }
    public Dictionary<string, JsonElement>? Parameters { get; init; }
    public List<string>? Aliases { get; init; }
}

[Register]
public sealed partial class PluginCommandRegistry : IPluginCommandRegistry
{
    private readonly ConcurrentDictionary<string, PluginCommandDefinition> _commands;
    [Inject] private readonly ILogger<PluginCommandRegistry>? _logger;
    private readonly ITelemetryService? _telemetryService;

    public PluginCommandRegistry(ILogger<PluginCommandRegistry>? logger = null, ITelemetryService? telemetryService = null)
    {
        _commands = new ConcurrentDictionary<string, PluginCommandDefinition>(StringComparer.OrdinalIgnoreCase);
        _logger = logger;
        _telemetryService = telemetryService;
    }

    public async Task RegisterCommandAsync(PluginCommandDefinition command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (_commands.TryAdd(command.CommandName, command))
        {
            RecordCommandRegistryMetrics("register", command.CommandName, true);
            _logger?.LogInformation(
                "[PluginCommandRegistry] 注册命令: {Command} (插件: {Plugin}, 类型: {HandlerType})",
                command.CommandName, command.PluginName, command.HandlerType);

            if (command.Aliases is { Count: > 0 })
            {
                foreach (var alias in command.Aliases)
                {
                    var aliasDef = new PluginCommandDefinition
                    {
                        CommandName = alias,
                        PluginName = command.PluginName,
                        Description = command.Description,
                        HandlerType = command.HandlerType,
                        Parameters = command.Parameters
                    };

                    _commands.TryAdd(alias, aliasDef);
                }
            }
        }
        else
        {
            RecordCommandRegistryMetrics("register", command.CommandName, false);
            _logger?.LogWarning("[PluginCommandRegistry] 命令 '{Command}' 已存在，跳过注册", command.CommandName);
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    public async Task UnregisterCommandAsync(string commandName, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandName);

        if (_commands.TryRemove(commandName, out var command))
        {
            RecordCommandRegistryMetrics("unregister", commandName, true);
            _logger?.LogInformation("[PluginCommandRegistry] 注销命令: {Command} (插件: {Plugin})",
                commandName, command.PluginName);

            if (command.Aliases is { Count: > 0 })
            {
                foreach (var alias in command.Aliases)
                {
                    _commands.TryRemove(alias, out _);
                }
            }
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    public IReadOnlyList<PluginCommandDefinition> GetRegisteredCommands()
    {
        return _commands.Values.ToList();
    }

    public PluginCommandDefinition? GetCommand(string commandName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandName);

        return _commands.TryGetValue(commandName, out var command) ? command : null;
    }

    private void RecordCommandRegistryMetrics(string operation, string commandName, bool isSuccess) =>
        _telemetryService?.RecordCount("plugin.command.count", new Dictionary<string, string> { ["operation"] = operation, ["command"] = commandName, ["success"] = isSuccess.ToString() }, "count", "Plugin command operation count");
}
