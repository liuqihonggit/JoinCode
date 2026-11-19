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
    public bool TryFindMatch(string objective, [NotNullWhen(true)] out GoalGraphTemplate? template) {
        ArgumentException.ThrowIfNullOrWhiteSpace(objective);
        foreach (var candidate in Volatile.Read(ref _templates).Values) {
            if (candidate.MatchesObjective(objective)) {
                template = candidate;
                return true;
            }
        }

        template = null;
        return false;
    }

    /// <inheritdoc />
    public GoalGraphTemplate[] GetAll() => Volatile.Read(ref _templates).Values.ToArray();
}