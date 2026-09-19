
namespace JoinCode.Abstractions.Models.Telemetry;

public enum TelemetrySpanKind {
    [EnumValue("internal")] Internal,
    [EnumValue("server")] Server,
    [EnumValue("client")] Client,
    [EnumValue("producer")] Producer,
    [EnumValue("consumer")] Consumer
}

public enum TelemetryStatusCode {
    [EnumValue("unset")] Unset,
    [EnumValue("ok")] Ok,
    [EnumValue("error")] Error
}

public enum TelemetryExportFormat {
    [EnumValue("none")] None,
    [EnumValue("otlp")] Otlp,
    [EnumValue("prometheus")] Prometheus,
    [EnumValue("console")] Console
}