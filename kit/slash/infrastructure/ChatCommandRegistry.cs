
namespace JoinCode.ChatCommands;

/// <summary>
/// 聊天命令注册表 — 管理斜杠命令的注册、查询、解析与分类，支持别名、缓存与遗留命令适配
/// </summary>
public sealed partial class ChatCommandRegistry : JoinCode.Abstractions.Interfaces.ICommandRegistry, ISlashCommandRegistry {
    private readonly CategorizedRegistry<string, IChatCommand, ChatCommandCategory> _registry;
    private readonly ILogger<ChatCommandRegistry>? _logger;
    private IReadOnlyList<ChatCommandInfo> _cachedCommandInfos = [];
    private bool _cachedCommandInfosValid;

    /// <summary>
    /// 构造 — 创建空注册表，命令名按 OrdinalIgnoreCase 比较
    /// </summary>
    /// <param name="logger">可选日志记录器</param>
    public ChatCommandRegistry(ILogger<ChatCommandRegistry>? logger = null) {
        _registry = new CategorizedRegistry<string, IChatCommand, ChatCommandCategory>(
            defaultCategory: ChatCommandCategory.Other,
            isEnabled: cmd => cmd.IsEnabled,
            comparer: StringComparer.OrdinalIgnoreCase);
        _logger = logger;
    }

    /// <summary>
    /// 注册命令 — 同名命令将被覆盖并记录警告，同时注册所有别名
    /// </summary>
    /// <param name="command">要注册的命令</param>
    public void Register(IChatCommand command) {
        if (_registry.ContainsKey(command.Name)) {
            _logger?.LogWarning("[ChatCommandRegistry] 命令 '{CommandName}' 已存在，将被覆盖", command.Name);
        }

        _registry.Register(command.Name, command, isCanonical: true);

        foreach (var alias in command.Aliases)
            _registry.RegisterAlias(alias, command);

        _cachedCommandInfosValid = false;
        _logger?.LogDebug("[ChatCommandRegistry] 已注册命令: {CommandName}", command.Name);
    }

    /// <summary>
    /// 注册命令分类 — 由源码生成器自动调用，特性解耦无需中央映射表
    /// </summary>
    public void SetCategory(string commandName, ChatCommandCategory category) => _registry.SetCategory(commandName, category);

    void JoinCode.Abstractions.Interfaces.ICommandRegistry.Register(JoinCode.Abstractions.Interfaces.ICommand command) {
        var adapter = new LegacyCommandAdapter(command);
        Register(adapter);
    }

    bool JoinCode.Abstractions.Interfaces.ICommandRegistry.UnregisterCommand(string commandName) {
        var removed = _registry.Unregister(commandName);
        if (removed) _cachedCommandInfosValid = false;
        return removed;
    }

    /// <summary>
    /// 批量注册命令
    /// </summary>
    /// <param name="commands">命令序列</param>
    public void RegisterRange(IEnumerable<IChatCommand> commands) {
        foreach (var command in commands)
            Register(command);
    }

    /// <summary>
    /// 按名称获取命令 — 未找到返回 null
    /// </summary>
    /// <param name="commandName">命令名称或别名</param>
    /// <returns>命令实例，未找到返回 null</returns>
    public IChatCommand? GetCommand(string commandName) {
        _registry.TryGetValue(commandName, out var cmd);
        return cmd;
    }

    /// <summary>
    /// 是否包含指定命令
    /// </summary>
    /// <param name="commandName">命令名称或别名</param>
    /// <returns>包含返回 true</returns>
    public bool HasCommand(string commandName) => _registry.ContainsKey(commandName);

    /// <summary>
    /// 获取所有规范化命令（不含别名）的只读字典
    /// </summary>
    /// <returns>命令名到命令实例的只读字典</returns>
    public IReadOnlyDictionary<string, IChatCommand> GetAllCommands() => _registry.GetAllCanonical();

    /// <summary>
    /// 解析用户输入 — 去除前导 / 后拆分命令名与参数
    /// </summary>
    /// <param name="input">原始用户输入</param>
    /// <returns>解析结果，包含命令名与参数</returns>
    public ChatCommandParseResult Parse(string input) {
        if (string.IsNullOrWhiteSpace(input)) {
            return ChatCommandParseResult.Failed("输入为空");
        }

        var trimmed = input.TrimStart('/');
        if (string.IsNullOrWhiteSpace(trimmed)) {
            return ChatCommandParseResult.Failed("命令名称为空");
        }

        var spaceIndex = trimmed.IndexOf(' ');
        string commandName;
        string arguments;

        if (spaceIndex == -1) {
            commandName = trimmed;
            arguments = string.Empty;
        } else {
            commandName = trimmed[..spaceIndex];
            arguments = trimmed[(spaceIndex + 1)..].Trim();
        }

        return ChatCommandParseResult.Success(commandName, arguments);
    }

    /// <summary>
    /// 获取所有命令信息（带分类）— 结果缓存，注册变更时自动失效
    /// </summary>
    /// <returns>命令信息列表</returns>
    public IEnumerable<ChatCommandInfo> GetCommandInfos() {
        if (!_cachedCommandInfosValid) {
            _cachedCommandInfos = _registry.GetCategorizedEntries()
                .Select(e => new ChatCommandInfo(
                    e.Value.Name,
                    e.Value.Description,
                    e.Value.Usage,
                    e.Value.Aliases,
                    e.Value.ArgumentHint,
                    e.Value.IsHidden || !e.IsEnabled,
                    e.Category))
                .ToArray();
            _cachedCommandInfosValid = true;
        }
        return _cachedCommandInfos;
    }
}

internal sealed class LegacyCommandAdapter : IChatCommand {
    private readonly JoinCode.Abstractions.Interfaces.ICommand _legacyCommand;

    public string Name => _legacyCommand.Name;
    public string Description => _legacyCommand.Description;
    public string Usage => _legacyCommand.Usage;
    public string[] Aliases => [];
    public string ArgumentHint => string.Empty;
    public bool IsHidden => false;
    public bool IsEnabled => true;

    public LegacyCommandAdapter(JoinCode.Abstractions.Interfaces.ICommand legacyCommand) {
        _legacyCommand = legacyCommand;
    }

    public async Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context) {
        var legacyContext = new LegacyCommandContext(context, _legacyCommand.Name);
        await _legacyCommand.ExecuteAsync(legacyContext, context.CancellationToken).ConfigureAwait(false);
        return ChatCommandResult.Continue();
    }
}

internal sealed class LegacyCommandContext : JoinCode.Abstractions.Interfaces.ICommandContext {
    private readonly ChatCommandContext _context;
    private readonly string _commandName;

    public LegacyCommandContext(ChatCommandContext context, string commandName) {
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
    public void OutputError(string message) => TerminalHelper.WriteLine($"{TerminalColors.Error}{message}{AnsiStyleEnumConstants.Reset}");
    public void OutputSuccess(string message) => TerminalHelper.WriteLine($"{TerminalColors.Success}{message}{AnsiStyleEnumConstants.Reset}");
    public void OutputWarning(string message) => TerminalHelper.WriteLine($"{TerminalColors.Warning}{message}{AnsiStyleEnumConstants.Reset}");
    public string? Prompt(string message) => _context.Prompt?.Invoke(message);
    public bool Confirm(string message) => _context.Confirm?.Invoke(message) ?? false;
    public void Output(string message, ConsoleColor color) => TerminalHelper.WriteLine(message);
    public string ReadPassword(string prompt) => _context.ReadPassword?.Invoke(prompt) ?? string.Empty;
}

internal sealed class LegacyConsoleOutput : JoinCode.Abstractions.Interfaces.IConsoleOutput {
    public void WriteLine(string message) => TerminalHelper.WriteLine(message);
    public void WriteError(string message) => TerminalHelper.WriteLine($"{TerminalColors.Error}{message}{AnsiStyleEnumConstants.Reset}");
    public void WriteSuccess(string message) => TerminalHelper.WriteLine($"{TerminalColors.Success}{message}{AnsiStyleEnumConstants.Reset}");
    public void WriteWarning(string message) => TerminalHelper.WriteLine($"{TerminalColors.Warning}{message}{AnsiStyleEnumConstants.Reset}");
    public string? Prompt(string message) {
        // 非交互模式或测试环境返回 null，避免无限等待
        if (Core.Utils.TestEnvironmentDetector.IsNonInteractive) {
            return null;
        } else {
            TerminalHelper.WriteRaw(message);
            return TerminalHelper.ReadLine();
        }
    }
    public bool Confirm(string message) {
        // 非交互模式或测试环境默认拒绝
        if (Core.Utils.TestEnvironmentDetector.IsNonInteractive) {
            return false;
        } else {
            TerminalHelper.WriteRaw($"{message} (y/N) ");
            return TerminalHelper.ReadLine()?.ToLowerInvariant() == "y";
        }
    }
    public void WriteLine(string message, ConsoleColor color) => TerminalHelper.WriteLine(message);
    public string ReadPassword(string prompt) {
        // 非交互模式或测试环境回退：返回空字符串
        if (Core.Utils.TestEnvironmentDetector.IsNonInteractive) {
            TerminalHelper.WriteLine(prompt);
            return string.Empty;
        }

        TerminalHelper.WriteRaw(prompt);
        var password = new System.Text.StringBuilder();
        while (true) {
            var key = TerminalHelper.ReadKey(true);
            if (key.Key == ConsoleKey.Enter) break;
            if (key.Key == ConsoleKey.Backspace) {
                if (password.Length > 0) password.Remove(password.Length - 1, 1);
            } else {
                password.Append(key.KeyChar);
            }
        }
        TerminalHelper.NewLine();
        return password.ToString();
    }
}

/// <summary>
/// 命令信息记录 — 包含命令元数据与分类，用于列表展示
/// </summary>
/// <param name="Name">命令名称</param>
/// <param name="Description">命令描述</param>
/// <param name="Usage">用法示例</param>
/// <param name="Aliases">命令别名数组</param>
/// <param name="ArgumentHint">参数提示文本</param>
/// <param name="IsHidden">是否隐藏</param>
/// <param name="Category">命令分类</param>
public sealed record ChatCommandInfo(string Name, string Description, string Usage, string[] Aliases, string ArgumentHint, bool IsHidden, ChatCommandCategory Category = ChatCommandCategory.Other) {
    /// <summary>
    /// 简化构造 — 别名空、参数提示空、不隐藏、分类为 Other
    /// </summary>
    /// <param name="Name">命令名称</param>
    /// <param name="Description">命令描述</param>
    /// <param name="Usage">用法示例</param>
    public ChatCommandInfo(string Name, string Description, string Usage) : this(Name, Description, Usage, [], string.Empty, false, ChatCommandCategory.Other) { }
}

/// <summary>
/// 命令解析结果 — 由 ChatCommandRegistry.Parse 产生，标记成功/失败及命令名、参数、错误信息
/// </summary>
public sealed partial class ChatCommandParseResult {
    /// <summary>是否解析成功</summary>
    public bool IsSuccess { get; private set; }
    /// <summary>命令名称（成功时有效）</summary>
    public string? CommandName { get; private set; }
    /// <summary>命令参数（成功时有效，已 Trim）</summary>
    public string Arguments { get; private set; } = string.Empty;
    /// <summary>错误信息（失败时有效）</summary>
    public string? ErrorMessage { get; private set; }

    private ChatCommandParseResult() { }

    /// <summary>
    /// 构造成功结果
    /// </summary>
    /// <param name="commandName">命令名称</param>
    /// <param name="arguments">命令参数</param>
    /// <returns>成功解析结果</returns>
    public static ChatCommandParseResult Success(string commandName, string arguments) => new() {
        IsSuccess = true,
        CommandName = commandName,
        Arguments = arguments
    };

    /// <summary>
    /// 构造失败结果
    /// </summary>
    /// <param name="errorMessage">错误信息</param>
    /// <returns>失败解析结果</returns>
    public static ChatCommandParseResult Failed(string errorMessage) => new() {
        IsSuccess = false,
        ErrorMessage = errorMessage
    };
}