using Microsoft.Extensions.Logging;
using CrossProcessChild;

var chainId = GetArg(args, "--chain");
var role = GetArg(args, "--role") ?? "client";
var workingDir = GetArg(args, "--workdir") ?? Path.GetTempPath();
var processId = int.Parse(GetArg(args, "--id") ?? "0");
var writeCount = int.Parse(GetArg(args, "--count") ?? "20");

var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Information);
});
var logger = loggerFactory.CreateLogger("CrossProcessChild");

var exportFormat = Environment.GetEnvironmentVariable("JCC_TELEMETRY_EXPORT") == "Console"
    ? TelemetryExportFormat.Console
    : TelemetryExportFormat.None;

var telemetry = new TelemetryService(new TelemetryConfig
{
    ServiceName = "CrossProcessChild",
    ExportFormat = exportFormat,
    TracingEnabled = true,
    MetricsEnabled = false
}, logger);

Console.WriteLine($"[Child] chain={chainId} role={role} id={processId} workdir={workingDir} count={writeCount}");

try
{
    switch (chainId)
    {
        case "1":
            await ChainOperations.RunLink001Async(workingDir, processId, writeCount, telemetry);
            break;
        case "2":
            await ChainOperations.RunLink002Async(workingDir, processId, writeCount, telemetry);
            break;
        case "3":
            await ChainOperations.RunLink003Async(workingDir, processId, writeCount, telemetry);
            break;
        case "4":
            await ChainOperations.RunLink004Async(workingDir, processId, writeCount, telemetry);
            break;
        case "5":
            await ChainOperations.RunLink005Async(workingDir, processId, writeCount, telemetry);
            break;
        default:
            Console.Error.WriteLine($"Unknown chain: {chainId}");
            Environment.ExitCode = 1;
            break;
    }

    Console.WriteLine($"[Child] DONE chain={chainId} id={processId}");
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[Child] ERROR chain={chainId} id={processId}: {ex.Message}");
    Environment.ExitCode = 1;
}
finally
{
    telemetry.Dispose();
    loggerFactory.Dispose();
}

static string? GetArg(string[] args, string name)
{
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
            return args[i + 1];
    }
    return null;
}
