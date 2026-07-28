

namespace McpToolHandlers;

[McpToolHandler(ToolCategory.Repl, Optional = true)]
public partial class ReplToolHandlers
{
    [Inject] private readonly ILogger<ReplToolHandlers>? _logger;
    private readonly IReplService? _replService;

    public ReplToolHandlers(ILogger<ReplToolHandlers>? logger = null, IReplService? replService = null)
    {
        _logger = logger;
        _replService = replService;
    }

    [McpTool(SystemToolNameConstants.Repl, "Execute code in REPL interactive mode", "execution")]
    public async Task<ToolResult> ReplAsync(
        [McpToolParameter("REPL language: csharp/powershell/python (default: csharp)", Required = false)] string language = "csharp",
        [McpToolParameter("Code to execute (optional, shows REPL status if not provided)", Required = false)] string? code = null,
        [McpToolParameter("Timeout in seconds (optional, default: 30)", Required = false)] int? timeout_seconds = 30,
        [McpToolParameter("Action: execute/enable/disable/status (default: execute)", Required = false)] string action = "execute",
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (_replService == null)
            {
                return McpResultBuilder.Error()
                    .WithText(L.T(StringKey.ReplServiceNotEnabled))
                    .Build();
            }

            var replAction = ReplActionExtensions.FromValue(action);
            if (replAction == null)
                return McpResultBuilder.Error().WithText($"Unknown REPL action: {action}").Build();

            var replLanguage = ReplLanguageExtensions.FromValue(language) ?? ReplLanguage.CSharp;

            switch (replAction.Value)
            {
                case ReplAction.Enable:
                    _replService.EnableReplMode();
                    return McpResultBuilder.Success()
                        .WithText(L.T(StringKey.ReplModeEnabled))
                        .Build();

                case ReplAction.Disable:
                    _replService.DisableReplMode();
                    return McpResultBuilder.Success()
                        .WithText(L.T(StringKey.ReplModeDisabled))
                        .Build();

                case ReplAction.Status:
                    var statusResponse = new System.Text.StringBuilder();
                    statusResponse.AppendLine(L.T(StringKey.ReplLabelMode, _replService.IsReplModeEnabled ? L.T(StringKey.ReplEnabled) : L.T(StringKey.ReplDisabled)));
                    if (_replService.IsReplModeEnabled)
                    {
                        statusResponse.AppendLine(L.T(StringKey.ReplLabelHiddenTools));
                        foreach (var tool in _replService.GetHiddenTools())
                        {
                            statusResponse.AppendLine($"  - {tool}");
                        }
                    }
                    return McpResultBuilder.Success().WithText(statusResponse.ToString()).Build();
            }

            if (string.IsNullOrEmpty(code))
            {
                var infoResponse = new System.Text.StringBuilder();
                infoResponse.AppendLine(L.T(StringKey.ReplLabelLanguage, replLanguage.ToValue()));
                infoResponse.AppendLine(L.T(StringKey.ReplLabelMode, _replService.IsReplModeEnabled ? L.T(StringKey.ReplEnabled) : L.T(StringKey.ReplDisabled)));
                infoResponse.AppendLine();
                infoResponse.AppendLine(L.T(StringKey.ProvideCodeOrManageMode));
                return McpResultBuilder.Success().WithText(infoResponse.ToString()).Build();
            }

            var result = await _replService.ExecuteAsync(code, replLanguage.ToValue(), timeout_seconds ?? 30, cancellationToken).ConfigureAwait(false);

            var response = new System.Text.StringBuilder();
            response.AppendLine(L.T(StringKey.ReplResultLabel, result.Language, result.Success ? L.T(StringKey.ReplSuccess) : L.T(StringKey.ReplFailed)));
            response.AppendLine(L.T(StringKey.TerminalLabelExecutionTime, result.ExecutionTime.TotalMilliseconds.ToString("F0")));
            response.AppendLine();

            if (!string.IsNullOrEmpty(result.Output))
            {
                response.AppendLine(L.T(StringKey.TerminalLabelOutput));
                response.AppendLine(result.Output);
            }

            if (!string.IsNullOrEmpty(result.Error))
            {
                response.AppendLine(L.T(StringKey.TerminalLabelError));
                response.AppendLine(result.Error);
            }

            return result.Success
                ? McpResultBuilder.Success().WithText(response.ToString()).Build()
                : McpResultBuilder.Error().WithText(response.ToString()).Build();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger?.LogError(ex, L.T(StringKey.ReplExecutionFailedLog));
            return McpResultBuilder.Error().WithText(L.T(StringKey.ReplExecutionFailed, ex.Message)).Build();
        }
    }
}
