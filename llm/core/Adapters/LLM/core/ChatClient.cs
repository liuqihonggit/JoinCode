
namespace Api.LLM;

[Register(typeof(IChatClient), ServiceLifetime.Singleton)]
public sealed partial class ChatClient : ServiceEntity, IChatClient {
    private readonly IQueryService _chatCompletionService;
    private readonly ToolCollection _plugins;

    /// <summary>构造聊天客户端。</summary>
    /// <param name="chatCompletionService">聊天补全服务。</param>
    public ChatClient(IQueryService chatCompletionService) {
        _chatCompletionService = chatCompletionService ?? throw new ArgumentNullException(nameof(chatCompletionService));
        _plugins = new ToolCollection();
    }

    /// <summary>获取聊天补全服务。</summary>
    public IQueryService GetChatCompletionService() => _chatCompletionService;

    /// <summary>获取插件集合。</summary>
    public IToolCollection Plugins => _plugins;
}

internal sealed class ToolCollection : IToolCollection {
    private readonly Dictionary<string, IToolGroup> _plugins = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>根据名称获取插件。</summary>
    /// <param name="name">插件名称。</param>
    public IToolGroup? GetPlugin(string name) {
        return _plugins.TryGetValue(name, out var plugin) ? plugin : null;
    }

    /// <summary>添加插件。</summary>
    /// <param name="plugin">插件组。</param>
    public void Add(IToolGroup plugin) {
        _plugins[plugin.Name] = plugin;
    }

    /// <summary>移除插件。</summary>
    /// <param name="name">插件名称。</param>
    public bool Remove(string name) {
        return _plugins.Remove(name);
    }

    /// <summary>获取插件名称集合。</summary>
    public IEnumerable<string> PluginNames => _plugins.Keys;
}

public sealed class ToolGroup : IToolGroup {
    private readonly List<IToolDef> _functions;

    /// <summary>构造工具组。</summary>
    /// <param name="name">工具组名称。</param>
    /// <param name="functions">工具函数集合。</param>
    public ToolGroup(string name, IEnumerable<IToolDef> functions) {
        Name = name;
        _functions = [.. functions];
    }

    /// <summary>获取工具组名称。</summary>
    public string Name { get; }

    /// <summary>获取工具函数集合。</summary>
    public IEnumerable<IToolDef> Functions => _functions;
}

public sealed class ToolDef : IToolDef {
    /// <summary>获取工具名称。</summary>
    public string Name { get; }
    /// <summary>获取工具描述。</summary>
    public string Description { get; }
    /// <summary>获取工具参数列表。</summary>
    public IReadOnlyList<IToolParam> Parameters { get; }

    /// <summary>构造工具定义。</summary>
    /// <param name="name">工具名称。</param>
    /// <param name="description">工具描述。</param>
    /// <param name="parameters">工具参数列表。</param>
    public ToolDef(string name, string description, IReadOnlyList<IToolParam>? parameters = null) {
        Name = name;
        Description = description;
        Parameters = parameters ?? [];
    }
}

public sealed class ToolParam : IToolParam {
    /// <summary>获取参数名称。</summary>
    public string Name { get; }
    /// <summary>获取参数描述。</summary>
    public string Description { get; }
    /// <summary>获取参数类型。</summary>
    public Type? ParameterType { get; }
    /// <summary>获取是否必填。</summary>
    public bool IsRequired { get; }

    /// <summary>构造工具参数。</summary>
    /// <param name="name">参数名称。</param>
    /// <param name="description">参数描述。</param>
    /// <param name="parameterType">参数类型。</param>
    /// <param name="isRequired">是否必填。</param>
    public ToolParam(string name, string description = "", Type? parameterType = null, bool isRequired = false) {
        Name = name;
        Description = description;
        ParameterType = parameterType;
        IsRequired = isRequired;
    }
}