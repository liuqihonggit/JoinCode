
namespace Api.LLM;

[Register]
public sealed partial class ChatClient : IChatClient
{
    private readonly IQueryService _chatCompletionService;
    private readonly ToolCollection _plugins;

    public ChatClient(IQueryService chatCompletionService)
    {
        _chatCompletionService = chatCompletionService ?? throw new ArgumentNullException(nameof(chatCompletionService));
        _plugins = new ToolCollection();
    }

    public IQueryService GetChatCompletionService() => _chatCompletionService;

    public IToolCollection Plugins => _plugins;
}

internal sealed class ToolCollection : IToolCollection
{
    private readonly Dictionary<string, IToolGroup> _plugins = new(StringComparer.OrdinalIgnoreCase);

    public IToolGroup? GetPlugin(string name)
    {
        return _plugins.TryGetValue(name, out var plugin) ? plugin : null;
    }

    public void Add(IToolGroup plugin)
    {
        _plugins[plugin.Name] = plugin;
    }

    public bool Remove(string name)
    {
        return _plugins.Remove(name);
    }

    public IReadOnlyList<string> PluginNames => _plugins.Keys.ToList();
}

public sealed class ToolGroup : IToolGroup
{
    private readonly List<IToolDef> _functions;

    public ToolGroup(string name, IEnumerable<IToolDef> functions)
    {
        Name = name;
        _functions = [.. functions];
    }

    public string Name { get; }

    public IReadOnlyList<IToolDef> Functions => _functions;
}

public sealed class ToolDef : IToolDef
{
    public string Name { get; }
    public string Description { get; }
    public IReadOnlyList<IToolParam> Parameters { get; }

    public ToolDef(string name, string description, IReadOnlyList<IToolParam>? parameters = null)
    {
        Name = name;
        Description = description;
        Parameters = parameters ?? [];
    }
}

public sealed class ToolParam : IToolParam
{
    public string Name { get; }
    public string Description { get; }
    public Type? ParameterType { get; }
    public bool IsRequired { get; }

    public ToolParam(string name, string description = "", Type? parameterType = null, bool isRequired = false)
    {
        Name = name;
        Description = description;
        ParameterType = parameterType;
        IsRequired = isRequired;
    }
}
