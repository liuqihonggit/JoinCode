namespace JoinCode.Abstractions.Security.Sandbox;

[Flags]
public enum SandboxCapabilities
{
    None = 0,
    PathRedirection = 1,
    FileSystemIsolation = 2,
    NetworkIsolation = 4,
    ProcessIsolation = 8,
    MemoryLimit = 16,
    CpuLimit = 32,
    TimeLimit = 64,
    UserNamespace = 128,
    FullIsolation = PathRedirection | FileSystemIsolation | NetworkIsolation | ProcessIsolation | MemoryLimit | CpuLimit | TimeLimit | UserNamespace
}
