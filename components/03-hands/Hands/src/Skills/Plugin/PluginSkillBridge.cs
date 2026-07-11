
namespace Core.Skills.Plugin;

[Register]
public sealed partial class PluginSkillBridge : IPluginSkillBridge
{
    private readonly IPluginManager _pluginManager;
    private readonly ISkillService _skillService;
    [Inject] private readonly ILogger<PluginSkillBridge>? _logger;
    private readonly ConcurrentDictionary<string, List<string>> _pluginSkillMap;
    private bool _isDisposed;

    public PluginSkillBridge(
        IPluginManager pluginManager,
        ISkillService skillService,
        ILogger<PluginSkillBridge>? logger = null)
    {
        Console.Error.WriteLine("[BRIDGE-CTOR] start");
        _pluginManager = pluginManager;
        _skillService = skillService;
        _logger = logger;
        _pluginSkillMap = new ConcurrentDictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        Console.Error.WriteLine("[BRIDGE-CTOR] done");
    }

    public async Task RegisterPluginSkillsAsync(string pluginName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(pluginName);

        if (_pluginSkillMap.ContainsKey(pluginName))
        {
            _logger?.LogWarning(L.T(StringKey.PluginSkillAlreadyRegistered), pluginName);
            return;
        }

        if (!_pluginManager.IsPluginLoaded(pluginName))
        {
            _logger?.LogWarning(L.T(StringKey.PluginSkillPluginNotLoaded), pluginName);
            return;
        }

        var skills = ExtractPluginSkills(pluginName);
        var registeredSkillNames = new List<string>();

        foreach (var skill in skills)
        {
            try
            {
                _skillService.RegisterSkill(skill);
                registeredSkillNames.Add(skill.Name);
                _logger?.LogInformation("[PluginSkillBridge] 注册插件技能: {Plugin}/{Skill}", pluginName, skill.Name);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "[PluginSkillBridge] 注册插件技能失败: {Plugin}/{Skill}", pluginName, skill.Name);
            }
        }

        _pluginSkillMap[pluginName] = registeredSkillNames;

        _logger?.LogInformation("[PluginSkillBridge] 插件 {Plugin} 注册 {Count} 个技能", pluginName, registeredSkillNames.Count);

        await Task.CompletedTask.ConfigureAwait(false);
    }

    public async Task UnregisterPluginSkillsAsync(string pluginName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(pluginName);

        if (!_pluginSkillMap.TryRemove(pluginName, out var skillNames))
        {
            _logger?.LogWarning("[PluginSkillBridge] 插件 {Plugin} 没有注册的技能", pluginName);
            return;
        }

        foreach (var skillName in skillNames)
        {
            try
            {
                _skillService.UnregisterSkill(skillName);
                _logger?.LogInformation("[PluginSkillBridge] 注销插件技能: {Plugin}/{Skill}", pluginName, skillName);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "[PluginSkillBridge] 注销插件技能失败: {Plugin}/{Skill}", pluginName, skillName);
            }
        }

        _logger?.LogInformation("[PluginSkillBridge] 插件 {Plugin} 注销 {Count} 个技能", pluginName, skillNames.Count);

        await Task.CompletedTask.ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SkillDefinition>> GetPluginSkillsAsync(string pluginName)
    {
        ArgumentException.ThrowIfNullOrEmpty(pluginName);

        if (!_pluginSkillMap.TryGetValue(pluginName, out var skillNames))
        {
            return Array.Empty<SkillDefinition>();
        }

        var skills = new List<SkillDefinition>();
        foreach (var skillName in skillNames)
        {
            var skill = await _skillService.GetSkillAsync(skillName).ConfigureAwait(false);
            if (skill != null)
            {
                skills.Add(skill);
            }
        }

        return skills;
    }

    public IReadOnlyList<string> GetPluginsWithSkills()
    {
        return _pluginSkillMap.Keys.ToList();
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        foreach (var pluginName in _pluginSkillMap.Keys.ToList())
        {
            try
            {
                var skillNames = _pluginSkillMap[pluginName];
                foreach (var skillName in skillNames)
                {
                    _skillService.UnregisterSkill(skillName);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "[PluginSkillBridge] 释放插件 {Plugin} 技能时出错", pluginName);
            }
        }

        _pluginSkillMap.Clear();
    }

    private List<SkillDefinition> ExtractPluginSkills(string pluginName)
    {
        var skills = new List<SkillDefinition>();

        var workflowPlugin = _pluginManager.GetWorkflowPlugin(pluginName);
        if (workflowPlugin != null)
        {
            var pluginSkills = ExtractFromWorkflowPlugin(pluginName, workflowPlugin);
            skills.AddRange(pluginSkills);
        }

        if (skills.Count == 0)
        {
            var defaultSkill = CreateDefaultPluginSkill(pluginName);
            skills.Add(defaultSkill);
        }

        return skills;
    }

    private static List<SkillDefinition> ExtractFromWorkflowPlugin(string pluginName, WorkflowPluginHost host)
    {
        var skills = new List<SkillDefinition>();
        var plugin = host.Plugin;

        var skill = new SkillDefinition
        {
            Name = $"plugin_{plugin.Name}",
            Description = plugin.Description,
            Version = plugin.Version,
            Parameters = new Dictionary<string, SkillParameter>
            {
                ["action"] = new() { Type = "string", Description = "要执行的操作", Required = true },
                ["input"] = new() { Type = "string", Description = "操作输入", Required = false }
            },
            Steps = new List<SkillStep>
            {
                new() { Id = "execute", Type = SkillStepType.Prompt, Description = $"执行插件 {plugin.Name}", Prompt = $"执行插件 {plugin.Name} 的操作: {{action}}，输入: {{input}}" }
            },
            Tags = new List<string> { "plugin", pluginName }.AsReadOnly(),
            Namespace = $"plugin.{pluginName}"
        };

        skills.Add(skill);
        return skills;
    }

    private static SkillDefinition CreateDefaultPluginSkill(string pluginName)
    {
        return new SkillDefinition
        {
            Name = $"plugin_{pluginName}",
            Description = $"插件 {pluginName} 提供的技能",
            Version = "1.0",
            Parameters = new Dictionary<string, SkillParameter>
            {
                ["action"] = new() { Type = "string", Description = "要执行的操作", Required = true },
                ["input"] = new() { Type = "string", Description = "操作输入", Required = false }
            },
            Steps = new List<SkillStep>
            {
                new() { Id = "execute", Type = SkillStepType.Prompt, Description = $"执行插件 {pluginName}", Prompt = $"执行插件 {pluginName} 的操作: {{{{action}}}}" }
            },
            Tags = new List<string> { "plugin", pluginName }.AsReadOnly(),
            Namespace = $"plugin.{pluginName}"
        };
    }
}
