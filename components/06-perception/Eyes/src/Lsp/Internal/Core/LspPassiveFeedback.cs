namespace Services.Lsp.Internal;

public interface ILspPassiveFeedback
{
    void RegisterNotificationHandlers(ILspManager manager);
}

[Register]
public sealed partial class LspPassiveFeedback : ILspPassiveFeedback
{
    [Inject] private readonly ILspDiagnosticRegistry _diagnosticRegistry;
    [Inject] private readonly ILogger<LspPassiveFeedback>? _logger;

    public void RegisterNotificationHandlers(ILspManager manager)
    {
        var servers = manager.GetAllServers();
        var successCount = 0;

        foreach (var kvp in servers)
        {
            var serverName = kvp.Key;
            var serverInstance = kvp.Value;

            try
            {
                RegisterDiagnosticsHandler(serverName, serverInstance);
                RegisterWorkspaceConfigurationHandler(serverName, serverInstance);
                successCount++;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to register diagnostics handler for {ServerName}", serverName);
            }
        }

        _logger?.LogInformation("Registered LSP notification handlers for {SuccessCount}/{TotalCount} server(s)",
            successCount, servers.Count);
    }

    private void RegisterDiagnosticsHandler(string serverName, ILspServerInstance serverInstance)
    {
        serverInstance.OnNotification("textDocument/publishDiagnostics",
            async (node, ct) =>
            {
                try
                {
                    if (node is not JsonObject obj ||
                        !obj.TryGetPropertyValue("uri", out var uriNode) ||
                        !obj.TryGetPropertyValue("diagnostics", out var diagsNode))
                    {
                        _logger?.LogDebug("Invalid publishDiagnostics params from {ServerName}", serverName);
                        return;
                    }

                    var uri = uriNode?.GetValue<string>() ?? "";
                    var diagsArray = diagsNode as JsonArray;
                    if (diagsArray == null || diagsArray.Count == 0)
                    {
                        return;
                    }

                    var diagnosticFiles = FormatDiagnosticsForAttachment(uri, diagsArray);
                    if (diagnosticFiles.Diagnostics.Count == 0) return;

                    _diagnosticRegistry.RegisterPending(serverName, [diagnosticFiles]);
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug(ex, "Error processing diagnostics from {ServerName}", serverName);
                }

                await ValueTask.CompletedTask;
            });
    }

    private void RegisterWorkspaceConfigurationHandler(string serverName, ILspServerInstance serverInstance)
    {
        serverInstance.OnRequest("workspace/configuration",
            (requestId, node, ct) =>
            {
                _logger?.LogDebug("LSP: Received workspace/configuration request from {ServerName}", serverName);

                var result = new JsonArray();
                if (node is JsonObject pObj &&
                    pObj.TryGetPropertyValue("items", out var itemsNode) &&
                    itemsNode is JsonArray items)
                {
                    for (var i = 0; i < items.Count; i++)
                    {
                        result.Add(null);
                    }
                }

                return new ValueTask<JsonNode?>(result);
            });
    }

    private static LspDiagnosticFile FormatDiagnosticsForAttachment(string uri, JsonArray diagsArray)
    {
        var diagnostics = new List<LspDiagnosticItem>();

        foreach (var diagNode in diagsArray)
        {
            if (diagNode is not JsonObject diag) continue;

            var message = diag.TryGetPropertyValue("message", out var msgNode) ? msgNode?.GetValue<string>() : null;
            if (string.IsNullOrEmpty(message)) continue;

            var severity = MapLspSeverity(
                diag.TryGetPropertyValue("severity", out var sevNode) ? sevNode?.GetValue<int>() : null);

            LspRange? range = null;
            if (diag.TryGetPropertyValue("range", out var rangeNode) && rangeNode is JsonObject rangeObj)
            {
                range = ParseRange(rangeObj);
            }

            var source = diag.TryGetPropertyValue("source", out var srcNode) ? srcNode?.GetValue<string>() : null;
            var code = diag.TryGetPropertyValue("code", out var codeNode) ? codeNode?.ToString() : null;

            diagnostics.Add(new LspDiagnosticItem
            {
                Message = message!,
                Severity = severity,
                Range = range,
                Source = source,
                Code = code
            });
        }

        var fileUri = uri.StartsWith("file://", StringComparison.OrdinalIgnoreCase)
            ? Uri.TryCreate(uri, UriKind.Absolute, out var u) ? u.LocalPath : uri
            : uri;

        return new LspDiagnosticFile
        {
            Uri = fileUri,
            Diagnostics = diagnostics
        };
    }

    private static string MapLspSeverity(int? lspSeverity) => lspSeverity switch
    {
        1 => "Error",
        2 => "Warning",
        3 => "Info",
        4 => "Hint",
        _ => "Error"
    };

    private static LspRange ParseRange(JsonObject rangeObj)
    {
        var start = rangeObj.TryGetPropertyValue("start", out var startNode) && startNode is JsonObject sObj
            ? ParsePosition(sObj)
            : new LspPosition();

        var end = rangeObj.TryGetPropertyValue("end", out var endNode) && endNode is JsonObject eObj
            ? ParsePosition(eObj)
            : new LspPosition();

        return new LspRange { Start = start, End = end };
    }

    private static LspPosition ParsePosition(JsonObject posObj)
    {
        var line = posObj.TryGetPropertyValue("line", out var lineNode) ? lineNode?.GetValue<int>() ?? 0 : 0;
        var character = posObj.TryGetPropertyValue("character", out var charNode) ? charNode?.GetValue<int>() ?? 0 : 0;
        return new LspPosition { Line = line, Character = character };
    }
}
