
namespace JoinCode.Abstractions.Models.Telemetry;

public enum TelemetrySpanKind
{
    Internal,
    Server,
    Client,
    Producer,
    Consumer
}

public enum TelemetryStatusCode
{
    Unset,
    Ok,
    Error
}

public enum TelemetryExportFormat
{
    None,
    Otlp,
    Prometheus,
    Console
}
