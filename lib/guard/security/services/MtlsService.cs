namespace Core.Security.Services;

/// <summary>
/// 双向 TLS (mTLS) 服务 — 管理客户端证书并提供 mTLS 配置
/// </summary>
[Register(typeof(IMtlsService), ServiceLifetime.Singleton)]
public sealed partial class MtlsService : ServiceEntity, IMtlsService
{
    private readonly IFileSystem _fs;
    private readonly ILogger<MtlsService>? _logger;
    private readonly ICaCertificateService? _caCertificateService;
    private readonly ITelemetryService? _telemetryService;
    private volatile MtlsConfiguration? _currentConfiguration;

    /// <summary>
    /// 构造 mTLS 服务
    /// </summary>
    /// <param name="fs">文件系统抽象</param>
    /// <param name="logger">日志器，可为空</param>
    /// <param name="caCertificateService">CA 证书服务，可为空</param>
    /// <param name="telemetryService">遥测服务，可为空</param>
    public MtlsService(IFileSystem fs, ILogger<MtlsService>? logger = null, ICaCertificateService? caCertificateService = null, ITelemetryService? telemetryService = null)
    {
        _fs = fs;
        _logger = logger;
        _caCertificateService = caCertificateService;
        _telemetryService = telemetryService;
    }

    /// <summary>
    /// 获取当前是否已成功配置 mTLS
    /// </summary>
    public bool IsMtlsConfigured => _currentConfiguration?.IsConfigured == true;

    /// <summary>
    /// 异步配置 mTLS — 加载客户端证书与可选 CA 证书，验证后缓存配置
    /// </summary>
    /// <param name="options">mTLS 配置选项</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>mTLS 配置结果，失败时 IsConfigured 为 false</returns>
    public async Task<MtlsConfiguration> ConfigureMtlsAsync(MtlsOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrEmpty(options.CertificatePath))
        {
            _logger?.LogWarning("[MtlsService] 未指定客户端证书路径");
            return new MtlsConfiguration { IsConfigured = false };
        }

        if (!_fs.FileExists(options.CertificatePath))
        {
            _logger?.LogError("[MtlsService] 客户端证书文件不存在: {Path}", options.CertificatePath);
            return new MtlsConfiguration { IsConfigured = false };
        }

        try
        {
            var clientCert = LoadCertificateFromFile(options.CertificatePath, options.CertificatePassword);
            if (clientCert is null)
            {
                _logger?.LogError("[MtlsService] 加载客户端证书失败");
                return new MtlsConfiguration { IsConfigured = false };
            }

            string? caThumbprint = null;
            if (_caCertificateService is not null && !string.IsNullOrEmpty(options.CaCertificatePath))
            {
                var caCert = _caCertificateService.LoadCertificate(options.CaCertificatePath);
                caThumbprint = caCert?.Thumbprint;
                if (caCert is not null)
                {
                    _caCertificateService.ValidateCertificate(caCert);
                }
            }
            else if (!string.IsNullOrEmpty(options.CaCertificatePath) && _fs.FileExists(options.CaCertificatePath))
            {
                var caCert = LoadCertificateFromFile(options.CaCertificatePath, null);
                caThumbprint = caCert?.Thumbprint;
            }

            var config = new MtlsConfiguration
            {
                IsConfigured = true,
                ClientCertificateThumbprint = clientCert.Thumbprint,
                ServerCaThumbprint = caThumbprint
            };

            _currentConfiguration = config;

            _logger?.LogInformation("[MtlsService] mTLS 配置成功 - 客户端证书指纹: {Thumbprint}",
                clientCert.Thumbprint);

            RecordMtlsMetrics("configure", true);

            await Task.CompletedTask.ConfigureAwait(false);
            return config;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[MtlsService] 配置 mTLS 失败");
            RecordMtlsMetrics("configure", false);
            return new MtlsConfiguration { IsConfigured = false };
        }
    }

    /// <summary>
    /// 创建带 mTLS 客户端证书与服务器 CA 校验回调的 HTTP 处理器
    /// </summary>
    /// <param name="config">mTLS 配置</param>
    /// <returns>配置好客户端证书的 HttpClientHandler</returns>
    public HttpClientHandler CreateMtlsHandler(MtlsConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var handler = new HttpClientHandler();

        if (!config.IsConfigured || _currentConfiguration is null)
        {
            return handler;
        }

        try
        {
            var clientCert = FindCertificateByThumbprint(config.ClientCertificateThumbprint);
            if (clientCert is not null)
            {
                handler.ClientCertificates.Add(clientCert);
                _logger?.LogDebug("[MtlsService] 已添加客户端证书到 Handler");
            }

            if (!string.IsNullOrEmpty(config.ServerCaThumbprint))
            {
                handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) =>
                {
                    if (cert is null) return false;

                    if (_caCertificateService is not null && chain is not null)
                    {
                        foreach (var element in chain.ChainElements)
                        {
                            if (element.Certificate is not null &&
                                _caCertificateService.ValidateCertificate(element.Certificate))
                            {
                                return true;
                            }
                        }

                        _logger?.LogWarning("[MtlsService] 服务器证书验证失败: CA 证书链验证未通过");
                        return false;
                    }

                    var elements = chain?.ChainElements;
                    if (elements is null) return false;

                    foreach (var ca in elements)
                    {
                        if (string.Equals(ca.Certificate?.Thumbprint, config.ServerCaThumbprint,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }

                    _logger?.LogWarning("[MtlsService] 服务器证书验证失败: CA 指纹不匹配");
                    return false;
                };
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[MtlsService] 创建 mTLS Handler 失败");
        }

        return handler;
    }

    private void RecordMtlsMetrics(string operation, bool isSuccess)
        => ToolTelemetryHelper.RecordToolCount(_telemetryService, "mtls.operation.count", operation, isSuccess, "mTLS operation count");

    private X509Certificate2? LoadCertificateFromFile(string path, string? password)
    {
        try
        {
            var data = _fs.ReadAllBytes(path);
            if (!string.IsNullOrEmpty(password))
            {
                return X509CertificateLoader.LoadPkcs12(data, password);
            }

            return X509CertificateLoader.LoadCertificate(data);
        }
        catch
        {
            return null;
        }
    }

    private static X509Certificate2? FindCertificateByThumbprint(string? thumbprint)
    {
        if (string.IsNullOrEmpty(thumbprint))
        {
            return null;
        }

        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadOnly);

        var certs = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, validOnly: false);
        return certs.Count > 0 ? certs[0] : null;
    }
}
