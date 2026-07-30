namespace McpToolDispatch;

using JoinCode.Abstractions.Security.Sandbox;

[McpToolDispatch(ToolCategory.Sandbox)]
public sealed class SandboxToolHandlers
{
    private readonly ISandboxManager _sandboxManager;

    public SandboxToolHandlers(ISandboxManager sandboxManager)
    {
        _sandboxManager = sandboxManager ?? throw new ArgumentNullException(nameof(sandboxManager));
    }

    [McpTool(SandboxToolNameConstants.SandboxEnter, "Enter a sandbox with the specified isolation type. Available types: soft (path redirection), process (OS-level process isolation), docker (container isolation), bubblewrap (Linux namespace isolation).", "sandbox")]
    public async Task<ToolResult> SandboxEnterAsync(
        [McpToolParameter("Sandbox type: soft, process, docker, or bubblewrap", Required = true, EnumValues = new[] { "soft", "process", "docker", "bubblewrap" })] string sandboxType,
        [McpToolParameter("Restrict file system access", Required = false, DefaultValue = "true")] string restrictFileSystem,
        [McpToolParameter("Restrict network access", Required = false, DefaultValue = "true")] string restrictNetwork,
        [McpToolParameter("Custom sandbox root path", Required = false)] string? sandboxRoot,
        [McpToolParameter("Memory limit in MB (process/docker only)", Required = false, DefaultValue = "0")] string memoryLimitMb,
        [McpToolParameter("CPU limit percent 1-100 (process/docker only)", Required = false, DefaultValue = "0")] string cpuLimitPercent,
        CancellationToken cancellationToken = default)
    {
        var type = SandboxTypeExtensions.FromValue(sandboxType);
        if (type is null)
        {
            return McpResultBuilder.Error()
                .WithText($"Unknown sandbox type: {sandboxType}. Available: soft, process, docker, bubblewrap")
                .Build();
        }

        if (!_sandboxManager.AvailableTypes.Contains(type.Value))
        {
            return McpResultBuilder.Error()
                .WithText($"Sandbox type '{sandboxType}' is not available on this platform. Available: {string.Join(", ", _sandboxManager.AvailableTypes.Select(t => t.ToValue()))}")
                .Build();
        }

        var options = new SandboxOptions
        {
            Type = type.Value,
            RestrictFileSystem = restrictFileSystem.Equals("true", StringComparison.OrdinalIgnoreCase),
            RestrictNetwork = restrictNetwork.Equals("true", StringComparison.OrdinalIgnoreCase),
            SandboxRoot = sandboxRoot,
            MemoryLimitMb = int.TryParse(memoryLimitMb, out var mem) ? mem : 0,
            CpuLimitPercent = int.TryParse(cpuLimitPercent, out var cpu) ? cpu : 0
        };

        try
        {
            var info = await _sandboxManager.EnterSandboxAsync(options, cancellationToken).ConfigureAwait(false);

            var response = new StringBuilder();
            response.AppendLine($"Sandbox activated: {info.Type.ToValue()}");
            response.AppendLine($"Sandbox ID: {info.SandboxId}");
            response.AppendLine($"Root path: {info.RootPath}");
            response.AppendLine($"Restricted: {info.IsRestricted}");
            response.AppendLine($"Capabilities: {info.Capabilities}");

            return McpResultBuilder.Success()
                .WithText(response.ToString())
                .Build();
        }
        catch (Exception ex)
        {
            return McpResultBuilder.Error()
                .WithText($"Failed to enter sandbox: {ex.Message}")
                .Build();
        }
    }

    [McpTool(SandboxToolNameConstants.SandboxExit, "Exit the current sandbox and restore normal access.", "sandbox")]
    public async Task<ToolResult> SandboxExitAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_sandboxManager.IsInSandbox)
        {
            return McpResultBuilder.Success()
                .WithText("Not in sandbox, nothing to exit")
                .Build();
        }

        try
        {
            var previousType = _sandboxManager.ActiveSandboxType;
            await _sandboxManager.ExitSandboxAsync(cancellationToken).ConfigureAwait(false);

            return McpResultBuilder.Success()
                .WithText($"Exited sandbox (was: {previousType.ToValue()})")
                .Build();
        }
        catch (Exception ex)
        {
            return McpResultBuilder.Error()
                .WithText($"Failed to exit sandbox: {ex.Message}")
                .Build();
        }
    }

    [McpTool(SandboxToolNameConstants.SandboxSwitch, "Switch to a different sandbox type while preserving isolation settings. Useful for escalating isolation level.", "sandbox")]
    public async Task<ToolResult> SandboxSwitchAsync(
        [McpToolParameter("Target sandbox type: soft, process, docker, or bubblewrap", Required = true, EnumValues = new[] { "soft", "process", "docker", "bubblewrap" })] string sandboxType,
        CancellationToken cancellationToken = default)
    {
        var type = SandboxTypeExtensions.FromValue(sandboxType);
        if (type is null)
        {
            return McpResultBuilder.Error()
                .WithText($"Unknown sandbox type: {sandboxType}")
                .Build();
        }

        if (!_sandboxManager.AvailableTypes.Contains(type.Value))
        {
            return McpResultBuilder.Error()
                .WithText($"Sandbox type '{sandboxType}' is not available on this platform")
                .Build();
        }

        try
        {
            var previousType = _sandboxManager.ActiveSandboxType;
            await _sandboxManager.SwitchProviderAsync(type.Value, cancellationToken).ConfigureAwait(false);

            return McpResultBuilder.Success()
                .WithText($"Sandbox switched: {previousType.ToValue()} → {type.Value.ToValue()}")
                .Build();
        }
        catch (Exception ex)
        {
            return McpResultBuilder.Error()
                .WithText($"Failed to switch sandbox: {ex.Message}")
                .Build();
        }
    }

    [McpTool(SandboxToolNameConstants.SandboxStatus, "Get the current sandbox status including type, isolation level, and available types.", "sandbox")]
    public Task<ToolResult> SandboxStatusAsync(
        CancellationToken cancellationToken = default)
    {
        var response = new StringBuilder();
        response.AppendLine($"In sandbox: {_sandboxManager.IsInSandbox}");

        if (_sandboxManager.IsInSandbox && _sandboxManager.CurrentSandbox is not null)
        {
            var info = _sandboxManager.CurrentSandbox;
            response.AppendLine($"Type: {info.Type.ToValue()}");
            response.AppendLine($"Sandbox ID: {info.SandboxId}");
            response.AppendLine($"Root path: {info.RootPath}");
            response.AppendLine($"Restricted: {info.IsRestricted}");
            response.AppendLine($"File system restricted: {info.RestrictFileSystem}");
            response.AppendLine($"Network restricted: {info.RestrictNetwork}");
            response.AppendLine($"Capabilities: {info.Capabilities}");
            if (info.AllowedPaths is not null && info.AllowedPaths.Count > 0)
            {
                response.AppendLine($"Allowed paths: {string.Join(", ", info.AllowedPaths)}");
            }
        }

        response.AppendLine($"Available types: {string.Join(", ", _sandboxManager.AvailableTypes.Select(t => t.ToValue()))}");

        return Task.FromResult(McpResultBuilder.Success()
            .WithText(response.ToString())
            .Build());
    }
}
