namespace JoinCode.Abstractions.Interfaces;

public interface IAgentToolRestrictions
{
    IReadOnlySet<string> GetAllowedTools(PermissionMode mode);
    IReadOnlySet<string> GetDeniedTools(PermissionMode mode);
    bool IsToolAllowedForMode(string toolName, PermissionMode mode);
}
