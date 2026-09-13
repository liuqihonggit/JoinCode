namespace JoinCode.Abstractions.Security.Sandbox.Ipc;

[JsonSerializable(typeof(SandboxIpcRequest))]
[JsonSerializable(typeof(SandboxIpcResponse))]
[JsonSerializable(typeof(SandboxExecuteRequest))]
[JsonSerializable(typeof(SandboxExecuteResponse))]
public sealed partial class SandboxIpcJsonContext : JsonSerializerContext;
