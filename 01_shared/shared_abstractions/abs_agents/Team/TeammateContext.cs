namespace JoinCode.Abstractions.Interfaces;

public sealed class TeammateContext
{
    private static readonly AsyncLocal<TeammateContext?> _current = new();

    public static TeammateContext? Current => _current.Value;

    public required string AgentId { get; init; }
    public required string AgentName { get; init; }
    public required string TeamName { get; init; }
    public string? Color { get; init; }
    public bool PlanModeRequired { get; init; }
    public required string ParentSessionId { get; init; }
    public bool IsInProcess { get; init; } = true;
    public string? TeamId { get; init; }

    public IDisposable EnterScope()
    {
        var previous = _current.Value;
        _current.Value = this;
        return new ScopeRestore(previous);
    }

    public Dictionary<string, string> ToEnvironmentVariables()
    {
        var env = new Dictionary<string, string>
        {
            [JccEnvVar.TeammateId.ToValue()] = AgentId,
            [JccEnvVar.TeammateName.ToValue()] = AgentName,
            [JccEnvVar.TeamName.ToValue()] = TeamName,
            [JccEnvVar.ParentSessionId.ToValue()] = ParentSessionId
        };

        if (!string.IsNullOrEmpty(Color))
            env[JccEnvVar.TeammateColor.ToValue()] = Color;

        if (!string.IsNullOrEmpty(TeamId))
            env[JccEnvVar.TeamId.ToValue()] = TeamId;

        if (PlanModeRequired)
            env[JccEnvVar.PlanModeRequired.ToValue()] = "1";

        if (IsInProcess)
            env[JccEnvVar.TeammateInProcess.ToValue()] = "1";

        return env;
    }

    private sealed class ScopeRestore(TeammateContext? previous) : IDisposable
    {
        public void Dispose() => _current.Value = previous;
    }
}
