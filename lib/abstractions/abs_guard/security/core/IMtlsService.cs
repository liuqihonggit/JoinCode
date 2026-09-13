namespace JoinCode.Abstractions.Security;

public interface IMtlsService
{
    Task<MtlsConfiguration> ConfigureMtlsAsync(MtlsOptions options, CancellationToken ct = default);
    HttpClientHandler CreateMtlsHandler(MtlsConfiguration config);
    bool IsMtlsConfigured { get; }
}

public sealed partial class MtlsOptions
{
    public string? CertificatePath { get; init; }
    public string? CertificatePassword { get; init; }
    public string? CaCertificatePath { get; init; }
    public bool ValidateServerCertificate { get; init; } = true;
}

public sealed partial class MtlsConfiguration
{
    public required bool IsConfigured { get; init; }
    public string? ClientCertificateThumbprint { get; init; }
    public string? ServerCaThumbprint { get; init; }
}
