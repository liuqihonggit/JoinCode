
namespace JoinCode.ChatCommands;

public sealed partial class ChatCommandRegistry : JoinCode.Abstractions.Interfaces.ICommandRegistry
{
    private readonly CategorizedRegistry<string, IChatCommand, ChatCommandCategory> _registry;
    [Inject] private readonly ILogger<ChatCommandRegistry>? _logger;
    private IReadOnlyList<ChatCommandInfo>? _cachedCommandInfos;

    public ChatCommandRegistry(ILogger<ChatCommandRegistry>? logger = null)
    {
        _registry = new CategorizedRegistry<string, IChatCommand, ChatCommandCategory>(
            defaultCategory: ChatCommandCategory.Other,
            isEnabled: cmd => cmd.IsEnabled,
            comparer: StringComparer.OrdinalIgnoreCase);
        _logger = logger;
    }

    public void Register(IChatCommand command)
    {
        if (_registry.ContainsKey(command.Name))
        {
            _logger?.LogWarning("[ChatCommandRegistry] 命令 '{CommandName}' 已存在，将被覆盖", command.Name);
        }

        _registry.Register(command.Name, command, isCanonical: true);

        foreach (var alias in command.Aliases)
            _registry.RegisterAlias(alias, command);

        _cachedCommandInfos = null;
        _logger?.LogDebug("[ChatCommandRegistry] 已注册命令: {CommandName}", command.Name);
    }

    /// <summary>
    /// 注册命令分类 — 由源码生成器自动调用，特性解耦无需中央映射表
    /// </summary>
    public void SetCategory(string commandName, ChatCommandCategory category) => _registry.SetCategory(commandName, category);

    void JoinCode.Abstractions.Interfaces.ICommandRegistry.Register(JoinCode.Abstractions.Interfaces.ICommand command)
    {
        var adapter = new LegacyCommandAdapter(command);
        Register(adapter);
    }

    bool JoinCode.Abstractions.Interfaces.ICommandRegistry.UnregisterCommand(string commandName)
    {
        var removed = _registry.Unregister(commandName);
        if (removed) _cachedCommandInfos = null;
        return removed;
    }

    public void RegisterRange(IEnumerable<IChatCommand> commands)
    {
        foreach (var command in commands)
            Register(command);
    }

    public IChatCommand? GetCommand(string commandName)
    {
        if (_registry.TryGetValue(commandName, out var cmd))
            return cmd;

        foreach (var entry in _registry.GetCategorizedEntries())
        {
            if (entry.Value.Aliases.Contains(commandName, StringComparer.OrdinalIgnoreCase))
                return entry.IsEnabled ? entry.Value : null;
        }
        return null;
    }

    public bool HasCommand(string commandName)
    {
        if (_registry.ContainsKey(commandName))
            return true;

        foreach (var entry in _registry.GetCategorizedEntries())
        {
            if (entry.Value.Aliases.Contains(commandName, StringComparer.OrdinalIgnoreCase))
                return entry.IsEnabled;
        }
        return false;
    }

    public IReadOnlyDictionary<string, IChatCommand> GetAllCommands() => _registry.GetAllCanonical();

    public ChatCommandParseResult Parse(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return ChatCommandParseResult.Failed("输入为空");
        }

        var trimmed = input.TrimStart('/');
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return ChatCommandParseResult.Failed("命令名称为空");
        }

        var spaceIndex = trimmed.IndexOf(' ');
        string commandName;
        string arguments;

        if (spaceIndex == -1)
        {
            commandName = trimmed;
            arguments = string.Empty;
        }
        else
        {
            commandName = trimmed[..spaceIndex];
            arguments = trimmed[(spaceIndex + 1)..].Trim();
        }

        return ChatCommandParseResult.Success(commandName, arguments);
    }

    public IReadOnlyList<ChatCommandInfo> GetCommandInfos()
    {
        return _cachedCommandInfos ??= _registry.GetCategorizedEntries()
            .Select(e => new ChatCommandInfo(
                e.Value.Name,
                e.Value.Description,
                e.Value.Usage,
                e.Value.Aliases,
                e.Value.ArgumentHint,
                e.Value.IsHidden || !e.IsEnabled,
                e.Category))
            .ToArray();
    }
}

internal sealed class LegacyCommandAdapter : IChatCommand
{
    private readonly JoinCode.Abstractions.Interfaces.ICommand _legacyCommand;

    public string Name => _legacyCommand.Name;
    public string Description => _legacyCommand.Description;
    public string Usage => _legacyCommand.Usage;
    public string[] Aliases => [];
    public string ArgumentHint => string.Empty;
    public bool IsHidden => false;
    public bool IsEnabled => true;

    public LegacyCommandAdapter(JoinCode.Abstractions.Interfaces.ICommand legacyCommand)
    {
        _legacyCommand = legacyCommand;
    }

    public async Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        var legacyContext = new LegacyCommandContext(context, _legacyCommand.Name);
        await _legacyCommand.ExecuteAsync(legacyContext, context.CancellationToken);
        return ChatCommandResult.Continue();
    }
}

internal sealed class LegacyCommandContext : JoinCode.Abstractions.Interfaces.ICommandContext
{
    private readonly ChatCommandContext _context;
    private readonly string _commandName;

    public LegacyCommandContext(ChatCommandContext context, string commandName)
    {
        _context = context;
        _commandName = commandName;
    }

    public string RawInput => "/" + _commandName + " " + _context.Arguments;
    public string CommandName => _commandName;
    public string[] Arguments => _context.Arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    public string SessionId => _context.SessionId;
    public ILogger Logger { get; } = Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
    public JoinCode.Abstractions.Interfaces.IConsoleOutput ConsoleOutput { get; } = new LegacyConsoleOutput();

    public void Output(string message) => TerminalHelper.WriteLine(message);
    public void OutputError(string message) => TerminalHelper.WriteLine($"{TerminalColors.Error}{message}{AnsiStyleConstants.Reset}");
    public void OutputSuccess(string message) => TerminalHelper.WriteLine($"{TerminalColors.Success}{message}{AnsiStyleConstants.Reset}");
    public void OutputWarning(string message) => TerminalHelper.WriteLine($"{TerminalColors.Warning}{message}{AnsiStyleConstants.Reset}");
    public string? Prompt(string message) => _context.Prompt?.Invoke(message);
    public bool Confirm(string message) => _context.Confirm?.Invoke(message) ?? false;
    public void Output(string message, ConsoleColor color) => TerminalHelper.WriteLine(message);
    public string ReadPassword(string prompt) => _context.ReadPassword?.Invoke(prompt) ?? string.Empty;
}

internal sealed class LegacyConsoleOutput : JoinCode.Abstractions.Interfaces.IConsoleOutput
{
    public void WriteLine(string message) => TerminalHelper.WriteLine(message);
    public void WriteError(string message) => TerminalHelper.WriteLine($"{TerminalColors.Error}{message}{AnsiStyleConstants.Reset}");
    public void WriteSuccess(string message) => TerminalHelper.WriteLine($"{TerminalColors.Success}{message}{AnsiStyleConstants.Reset}");
    public void WriteWarning(string message) => TerminalHelper.WriteLine($"{TerminalColors.Warning}{message}{AnsiStyleConstants.Reset}");
    public string? Prompt(string message)
    {
        // 非交互模式或测试环境返回 null，避免无限等待
        if (Core.Utils.TestEnvironmentDetector.IsNonInteractive)
        {
            return null;
        }
        else
        {
            TerminalHelper.WriteRaw(message);
            return TerminalHelper.ReadLine();
        }
    }
    public bool Confirm(string message)
    {
        // 非交互模式或测试环境默认拒绝
        if (Core.Utils.TestEnvironmentDetector.IsNonInteractive)
        {
            return false;
        }
        else
        {
            TerminalHelper.WriteRaw($"{message} (y/N) ");
            return TerminalHelper.ReadLine()?.ToLowerInvariant() == "y";
        }
    }
    public void WriteLine(string message, ConsoleColor color) => TerminalHelper.WriteLine(message);
    public string ReadPassword(string prompt)
    {
        // 非交互模式或测试环境回退：返回空字符串
        if (Core.Utils.TestEnvironmentDetector.IsNonInteractive)
        {
            TerminalHelper.WriteLine(prompt);
            return string.Empty;
        }
        else
        {
            TerminalHelper.WriteRaw(prompt);
            var password = new System.Text.StringBuilder();
            while (true)
            {
                var key = TerminalHelper.ReadKey(true);
                if (key.Key == ConsoleKey.Enter) break;
                if (key.Key == ConsoleKey.Backspace)
                {
                    if (password.Length > 0) password.Remove(password.Length - 1, 1);
                }
                else
                {
                    password.Append(key.KeyChar);
                }
            }
            TerminalHelper.NewLine();
            return password.ToString();
        }
    }
}

public sealed record ChatCommandInfo(string Name, string Description, string Usage, string[] Aliases, string ArgumentHint, bool IsHidden, ChatCommandCategory Category = ChatCommandCategory.Other)
{
    public ChatCommandInfo(string Name, string Description, string Usage) : this(Name, Description, Usage, [], string.Empty, false, ChatCommandCategory.Other) { }
}

public sealed partial class ChatCommandParseResult
{
    public bool IsSuccess { get; private set; }
    public string? CommandName { get; private set; }
    public string Arguments { get; private set; } = string.Empty;
    public string? ErrorMessage { get; private set; }

    private ChatCommandParseResult() { }

    public static ChatCommandParseResult Success(string commandName, string arguments) => new()
    {
        IsSuccess = true,
        CommandName = commandName,
        Arguments = arguments
    };

    public static ChatCommandParseResult Failed(string errorMessage) => new()
    {
        IsSuccess = false,
        ErrorMessage = errorMessage
    };
}
