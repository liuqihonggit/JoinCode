namespace Core.Security.Services;

public interface ICaCertificateService
{
    Task ConfigureCaCertificatesAsync(CaCertificateOptions options, CancellationToken ct = default);
    X509Certificate2? LoadCertificate(string path, string? password = null);
    bool ValidateCertificate(X509Certificate2 certificate);
}

public sealed partial class CaCertificateOptions
{
    public string? CaBundlePath { get; init; }
    public List<string>? AdditionalCaPaths { get; init; }
    public bool UseSystemStore { get; init; } = true;
}

[Register]
public sealed partial class CaCertificateService : ICaCertificateService
{
    private readonly IFileSystem _fs;
    [Inject] private readonly ILogger<CaCertificateService>? _logger;
    private readonly ITelemetryService? _telemetryService;
    private readonly List<X509Certificate2> _loadedCertificates;

    public CaCertificateService(IFileSystem fs, ILogger<CaCertificateService>? logger = null, ITelemetryService? telemetryService = null)
    {
        _fs = fs;
        _logger = logger;
        _telemetryService = telemetryService;
        _loadedCertificates = new List<X509Certificate2>();
    }

    public async Task ConfigureCaCertificatesAsync(CaCertificateOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        _loadedCertificates.Clear();

        if (options.UseSystemStore)
        {
            LoadSystemCertificates();
        }

        if (!string.IsNullOrEmpty(options.CaBundlePath) && _fs.FileExists(options.CaBundlePath))
        {
            LoadCaBundle(options.CaBundlePath);
        }

        if (options.AdditionalCaPaths is not null)
        {
            foreach (var caPath in options.AdditionalCaPaths)
            {
                if (_fs.FileExists(caPath))
                {
                    LoadSingleCertificate(caPath);
                }
                else
                {
                    _logger?.LogWarning("[CaCertificateService] CA 证书文件不存在: {Path}", caPath);
                }
            }
        }

        _logger?.LogInformation("[CaCertificateService] 已加载 {Count} 个 CA 证书", _loadedCertificates.Count);
        RecordCaMetrics("configure", _loadedCertificates.Count);

        await Task.CompletedTask.ConfigureAwait(false);
    }

    public X509Certificate2? LoadCertificate(string path, string? password = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!_fs.FileExists(path))
        {
            _logger?.LogWarning("[CaCertificateService] 证书文件不存在: {Path}", path);
            return null;
        }

        try
        {
            var data = _fs.ReadAllBytes(path);
            if (!string.IsNullOrEmpty(password))
            {
                return X509CertificateLoader.LoadPkcs12(data, password);
            }

            return X509CertificateLoader.LoadCertificate(data);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[CaCertificateService] 加载证书失败: {Path}", path);
            return null;
        }
    }

    public bool ValidateCertificate(X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);

        try
        {
            using var chain = new X509Chain();
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;

            if (_loadedCertificates.Count > 0)
            {
                foreach (var caCert in _loadedCertificates)
                {
                    chain.ChainPolicy.ExtraStore.Add(caCert);
                }
            }

            var isValid = chain.Build(certificate);

            if (!isValid)
            {
                var errors = chain.ChainStatus.Select(s => s.StatusInformation);
                _logger?.LogWarning("[CaCertificateService] 证书验证失败: {Errors}", string.Join(", ", errors));
            }

            return isValid;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[CaCertificateService] 证书验证异常");
            return false;
        }
    }

    private void RecordCaMetrics(string operation, int count)
    {
        _telemetryService?.RecordCount("ca.certificate.count", new() { ["operation"] = operation }, description: "CA certificate operation count");
        _telemetryService?.RecordHistogram("ca.certificate.loaded", count, new() { ["operation"] = operation }, "certs", "CA certificates loaded");
    }

    private void LoadSystemCertificates()
    {
        try
        {
            using var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadOnly);

            foreach (var cert in store.Certificates)
            {
                _loadedCertificates.Add(cert);
            }

            _logger?.LogDebug("[CaCertificateService] 从系统存储加载了 {Count} 个根证书", store.Certificates.Count);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[CaCertificateService] 加载系统证书存储失败");
        }
    }

    private void LoadCaBundle(string bundlePath)
    {
        try
        {
            var content = _fs.ReadAllText(bundlePath);
            var pemBlocks = ExtractPemBlocks(content, "CERTIFICATE");

            foreach (var pemBlock in pemBlocks)
            {
                try
                {
                    var certData = Convert.FromBase64String(pemBlock);
                    var cert = X509CertificateLoader.LoadCertificate(certData);
                    _loadedCertificates.Add(cert);
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug(ex, "[CaCertificateService] 解析 PEM 块失败，跳过");
                }
            }

            _logger?.LogDebug("[CaCertificateService] 从 CA Bundle 加载了 {Count} 个证书", pemBlocks.Count);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[CaCertificateService] 加载 CA Bundle 失败: {Path}", bundlePath);
        }
    }

    private void LoadSingleCertificate(string path)
    {
        var cert = LoadCertificate(path);
        if (cert is not null)
        {
            _loadedCertificates.Add(cert);
        }
    }

    private static List<string> ExtractPemBlocks(string pemContent, string label)
    {
        var blocks = new List<string>();
        var beginMarker = $"-----BEGIN {label}-----";
        var endMarker = $"-----END {label}-----";

        var startIndex = 0;
        while (true)
        {
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
