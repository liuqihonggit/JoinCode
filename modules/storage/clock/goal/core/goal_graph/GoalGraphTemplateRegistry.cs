// JCC11005 抑制: 存量返回 null 语义, 后续评估改 TryGet
#pragma warning disable JCC11005
namespace Core.Goal;

/// <summary>
/// Goal Graph 模板注册表默认实现 — 内部 ConcurrentDictionary，对外暴露遍历器 + 字典视图
/// </summary>
[Register(typeof(IGoalGraphTemplateRegistry), ServiceLifetime.Singleton)]
public sealed class GoalGraphTemplateRegistry : ServiceEntity, IGoalGraphTemplateRegistry {
    private ImmutableHamT<string, GoalGraphTemplate> _templates = ImmutableHamT<string, GoalGraphTemplate>.Empty.WithComparers(StringComparer.Ordinal);

    /// <inheritdoc />
    public void Register(GoalGraphTemplate template) {
        ArgumentNullException.ThrowIfNull(template);
        ImmutableInterlocked.Update(ref _templates, d => d.ContainsKey(template.Name) ? d : d.Add(template.Name, template));
    }

    /// <inheritdoc />
    public GoalGraphTemplate? FindMatch(string objective) {
        ArgumentException.ThrowIfNullOrWhiteSpace(objective);
        foreach (var template in Volatile.Read(ref _templates).Values) {
            if (template.MatchesObjective(objective))
                return template;
        }
        return null;
    }

    /// <inheritdoc />
    public GoalGraphTemplate[] GetAll() => Volatile.Read(ref _templates).Values.ToArray();
}