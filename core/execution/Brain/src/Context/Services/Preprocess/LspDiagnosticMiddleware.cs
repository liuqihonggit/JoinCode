using JoinCode.Abstractions.Attributes;

namespace Core.Context;

/// <summary>
/// LSP 诊断注入中间件 — 检查待处理的 LSP 诊断并注入提醒
/// </summary>
[Register(typeof(IPreparePreprocessMiddleware))]
public sealed partial class LspDiagnosticMiddleware : IPreparePreprocessMiddleware
{
    [Inject] private readonly JoinCode.Abstractions.Interfaces.Lsp.ILspDiagnosticProvider? _lspDiagnosticProvider;
    [Inject] private readonly ISystemReminderManager _reminderManager;
    [Inject] private readonly IChatContextManager _contextManager;

    public ErrorBehavior OnError => ErrorBehavior.Continue;

    /// <inheritdoc/>
    public async Task InvokeAsync(PreprocessContext context, MiddlewareDelegate<PreprocessContext> next, CancellationToken ct)
    {
        if (_lspDiagnosticProvider is not null)
        {
            var pendingDiagnostics = _lspDiagnosticProvider.CheckPendingDiagnostics();
            if (pendingDiagnostics.Count > 0)
            {
                var diagnosticText = FormatLspDiagnostics(pendingDiagnostics);
                context.LspDiagnosticText = diagnosticText;

                await _reminderManager.AddReminderAsync(
                    "lsp-diagnostics",
                    diagnosticText,
                    priority: 60,
                    ct: ct).ConfigureAwait(false);

                var updatedReminders = await _reminderManager.FormatAsSystemRemindersAsync().ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(updatedReminders))
                {
                    await _contextManager.AddDynamicSystemMessageAsync(updatedReminders, ct).ConfigureAwait(false);
                }
            }
        }

        await next(context, ct).ConfigureAwait(false);
    }

    private static string FormatLspDiagnostics(List<(string ServerName, List<JoinCode.Abstractions.Interfaces.Lsp.LspDiagnosticSummary> Files)> pendingDiagnostics)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<new-diagnostics>The following new diagnostic issues were detected:");
        sb.AppendLine();

        foreach (var (_, files) in pendingDiagnostics)
        {
            foreach (var file in files)
            {
                var filePath = file.Uri.StartsWith("file://", StringComparison.OrdinalIgnoreCase)
                    ? file.Uri[7..]
                    : file.Uri;

                foreach (var diag in file.Diagnostics)
                {
                    var severityIcon = diag.Severity switch
                    {
                        "Error" => "✗",
                        "Warning" => "⚠",
                        "Info" => "ℹ",
                        _ => "·"
                    };

                    var location = diag.StartLine.HasValue && diag.StartCharacter.HasValue
                        ? $"{diag.StartLine.Value + 1}:{diag.StartCharacter.Value + 1}"
                        : "?";

                    sb.Append($"  {severityIcon} {filePath}:{location} ");
                    sb.Append($"{diag.Severity ?? "Info"}: {diag.Message}");

                    if (!string.IsNullOrEmpty(diag.Source))
                        sb.Append($" [{diag.Source}]");
                    if (!string.IsNullOrEmpty(diag.Code))
                        sb.Append($" ({diag.Code})");

                    sb.AppendLine();
                }
            }
        }

        sb.AppendLine("</new-diagnostics>");
        return sb.ToString();
    }
}
