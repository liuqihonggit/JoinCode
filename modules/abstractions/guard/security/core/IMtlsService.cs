namespace JoinCode.Abstractions.Security;

public interface IMtlsService {
    /// <summary>异步根据选项配置双向 TLS。</summary>
    Task<MtlsConfiguration> ConfigureMtlsAsync(MtlsOptions options, CancellationToken ct = default);
    /// <summary>根据配置创建支持 mTLS 的 HttpClientHandler。</summary>
    HttpClientHandler CreateMtlsHandler(MtlsConfiguration config);
    /// <summary>获取是否已配置 mTLS。</summary>
    bool IsMtlsConfigured { get; }
}

public sealed partial class MtlsOptions {
    /// <summary>获取证书路径。</summary>
    public string? CertificatePath { get; init; }
    /// <summary>获取证书密码。</summary>
    public string? CertificatePassword { get; init; }
    /// <summary>获取 CA 证书路径。</summary>
    public string? CaCertificatePath { get; init; }
    /// <summary>获取是否校验服务器证书。</summary>
    public bool ValidateServerCertificate { get; init; } = true;
}

public sealed partial class MtlsConfiguration {
    /// <summary>获取是否已配置。</summary>
    public required bool IsConfigured { get; init; }
    /// <summary>获取客户端证书指纹。</summary>
    public string? ClientCertificateThumbprint { get; init; }
    /// <summary>获取服务器 CA 指纹。</summary>
    public string? ServerCaThumbprint { get; init; }
}
