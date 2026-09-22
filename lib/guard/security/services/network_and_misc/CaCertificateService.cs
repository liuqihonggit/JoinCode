namespace Core.Security.Services;

/// <summary>
/// CA 证书服务接口 — 提供 CA 证书配置、加载与验证能力,支持系统证书存储、CA Bundle 与额外证书文件
/// </summary>
public interface ICaCertificateService {
    /// <summary>
    /// 异步配置 CA 证书 — 按选项加载系统证书、CA Bundle 与额外证书文件
    /// </summary>
    /// <param name="options">CA 证书配置选项</param>
    /// <param name="ct">取消令牌</param>
    Task ConfigureCaCertificatesAsync(CaCertificateOptions options, CancellationToken ct = default);

    /// <summary>
    /// 从指定路径加载证书,可选密码用于 PKCS12 格式
    /// </summary>
    /// <param name="path">证书文件路径</param>
    /// <param name="password">证书密码(可选,仅 PKCS12 格式需要)</param>
    /// <returns>加载成功的证书实例;失败时返回 null</returns>
    ValueTask<X509Certificate2?> LoadCertificate(string path, string? password = null);

    /// <summary>
    /// 验证证书是否可信 — 基于已加载的 CA 证书构建证书链并校验
    /// </summary>
    /// <param name="certificate">待验证的证书</param>
    /// <returns>验证通过返回 true;否则返回 false</returns>
    bool ValidateCertificate(X509Certificate2 certificate);
}

/// <summary>
/// CA 证书配置选项 — 描述 CA Bundle 路径、额外证书路径与是否使用系统证书存储
/// </summary>
public sealed partial class CaCertificateOptions {
    /// <summary>CA Bundle 文件路径(可选)</summary>
    public string? CaBundlePath { get; init; }
    /// <summary>额外 CA 证书文件路径列表</summary>
    public List<string> AdditionalCaPaths { get; init; } = [];
    /// <summary>是否使用系统证书存储,默认为 true</summary>
    public bool UseSystemStore { get; init; } = true;
}

/// <summary>
/// CA 证书服务实现 — 基于系统存储、CA Bundle 与额外证书文件加载并验证证书链
/// </summary>
[Register(typeof(ICaCertificateService), ServiceLifetime.Singleton)]
public sealed partial class CaCertificateService : ServiceEntity, ICaCertificateService {
    private readonly IFileSystem _fs;
    private readonly ILogger<CaCertificateService>? _logger;
    private readonly ITelemetryService? _telemetryService;
    private readonly List<X509Certificate2> _loadedCertificates;

    /// <summary>
    /// 构造 CA 证书服务
    /// </summary>
    /// <param name="fs">文件系统抽象</param>
    /// <param name="logger">日志记录器(可选)</param>
    /// <param name="telemetryService">遥测服务(可选)</param>
    public CaCertificateService(IFileSystem fs, ILogger<CaCertificateService>? logger = null, ITelemetryService? telemetryService = null) {
        _fs = fs;
        _logger = logger;
        _telemetryService = telemetryService;
        _loadedCertificates = new List<X509Certificate2>();
    }

    /// <inheritdoc/>
    public async Task ConfigureCaCertificatesAsync(CaCertificateOptions options, CancellationToken ct = default) {
        ArgumentNullException.ThrowIfNull(options);

        _loadedCertificates.Clear();

        if (options.UseSystemStore) {
            LoadSystemCertificates();
        }

        if (!string.IsNullOrEmpty(options.CaBundlePath) && _fs.FileExists(options.CaBundlePath)) {
            await LoadCaBundleAsync(options.CaBundlePath).ConfigureAwait(false);
        }

        if (options.AdditionalCaPaths is not null) {
            foreach (var caPath in options.AdditionalCaPaths) {
                if (_fs.FileExists(caPath)) {
                    await LoadSingleCertificateAsync(caPath).ConfigureAwait(false);
                } else {
                    _logger?.LogWarning("[CaCertificateService] CA 证书文件不存在: {Path}", caPath);
                }
            }
        }

        _logger?.LogInformation("[CaCertificateService] 已加载 {Count} 个 CA 证书", _loadedCertificates.Count);
        RecordCaMetrics("configure", _loadedCertificates.Count);

        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask<X509Certificate2?> LoadCertificate(string path, string? password = null) {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!_fs.FileExists(path)) {
            _logger?.LogWarning("[CaCertificateService] 证书文件不存在: {Path}", path);
            return null;
        }

        try {
            var data = await _fs.ReadAllBytes(path).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(password)) {
                return X509CertificateLoader.LoadPkcs12(data, password);
            }

            return X509CertificateLoader.LoadCertificate(data);
        } catch (Exception ex) {
            _logger?.LogError(ex, "[CaCertificateService] 加载证书失败: {Path}", path);
            return null;
        }
    }

    /// <inheritdoc/>
    public bool ValidateCertificate(X509Certificate2 certificate) {
        ArgumentNullException.ThrowIfNull(certificate);

        try {
            using var chain = new X509Chain();
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;

            if (_loadedCertificates.Count > 0) {
                foreach (var caCert in _loadedCertificates) {
                    chain.ChainPolicy.ExtraStore.Add(caCert);
                }
            }

            var isValid = chain.Build(certificate);

            if (!isValid) {
                var errors = chain.ChainStatus.Select(s => s.StatusInformation);
                _logger?.LogWarning("[CaCertificateService] 证书验证失败: {Errors}", string.Join(", ", errors));
            }

            return isValid;
        } catch (Exception ex) {
            _logger?.LogError(ex, "[CaCertificateService] 证书验证异常");
            return false;
        }
    }

    private void RecordCaMetrics(string operation, int count) {
        _telemetryService?.RecordCount("ca.certificate.count", new() { ["operation"] = operation }, description: "CA certificate operation count");
        _telemetryService?.RecordHistogram("ca.certificate.loaded", count, new() { ["operation"] = operation }, "certs", "CA certificates loaded");
    }

    private void LoadSystemCertificates() {
        try {
            using var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadOnly);

            foreach (var cert in store.Certificates) {
                _loadedCertificates.Add(cert);
            }

            _logger?.LogDebug("[CaCertificateService] 从系统存储加载了 {Count} 个根证书", store.Certificates.Count);
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "[CaCertificateService] 加载系统证书存储失败");
        }
    }

    private async ValueTask LoadCaBundleAsync(string bundlePath) {
        try {
            var content = await _fs.ReadAllText(bundlePath).ConfigureAwait(false);
            var pemBlocks = ExtractPemBlocks(content, "CERTIFICATE");

            foreach (var pemBlock in pemBlocks) {
                try {
                    var certData = Convert.FromBase64String(pemBlock);
                    var cert = X509CertificateLoader.LoadCertificate(certData);
                    _loadedCertificates.Add(cert);
                } catch (Exception ex) {
                    _logger?.LogDebug(ex, "[CaCertificateService] 解析 PEM 块失败，跳过");
                }
            }

            _logger?.LogDebug("[CaCertificateService] 从 CA Bundle 加载了 {Count} 个证书", pemBlocks.Count);
        } catch (Exception ex) {
            _logger?.LogError(ex, "[CaCertificateService] 加载 CA Bundle 失败: {Path}", bundlePath);
        }
    }

    private async ValueTask LoadSingleCertificateAsync(string path) {
        var cert = await LoadCertificate(path).ConfigureAwait(false);
        if (cert is not null) {
            _loadedCertificates.Add(cert);
        }
    }

    private static List<string> ExtractPemBlocks(string pemContent, string label) {
        var blocks = new List<string>();
        var beginMarker = $"-----BEGIN {label}-----";
        var endMarker = $"-----END {label}-----";

        var startIndex = 0;
        while (true) {
            var beginIdx = pemContent.IndexOf(beginMarker, startIndex, StringComparison.Ordinal);
            if (beginIdx < 0) break;

            var endIdx = pemContent.IndexOf(endMarker, beginIdx, StringComparison.Ordinal);
            if (endIdx < 0) break;

            var base64Start = beginIdx + beginMarker.Length;
            var base64Content = pemContent[base64Start..endIdx].Trim();
            blocks.Add(base64Content);

            startIndex = endIdx + endMarker.Length;
        }

        return blocks;
    }
}