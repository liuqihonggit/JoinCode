namespace Guard.Tests.Security.Services;

public sealed class CaCertificateServiceTests
{
    private readonly IFileSystem _fs = TestFileSystem.Current;
    private readonly CaCertificateService _sut;

    public CaCertificateServiceTests()
    {
        _sut = new CaCertificateService(_fs, NullLogger<CaCertificateService>.Instance);
    }

    [Fact]
    public async Task ConfigureCaCertificatesAsync_NullOptions_ShouldThrowArgumentNullException()
    {
        var act = async () => await _sut.ConfigureCaCertificatesAsync(null!).ConfigureAwait(true);

        await act.Should().ThrowAsync<ArgumentNullException>().ConfigureAwait(true);
    }

    [Fact]
    public async Task ConfigureCaCertificatesAsync_UseSystemStore_ShouldNotThrow()
    {
        var options = new CaCertificateOptions
        {
            UseSystemStore = true
        };

        var act = async () => await _sut.ConfigureCaCertificatesAsync(options).ConfigureAwait(true);

        await act.Should().NotThrowAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task ConfigureCaCertificatesAsync_NoSystemStoreNoBundle_ShouldNotThrow()
    {
        var options = new CaCertificateOptions
        {
            UseSystemStore = false
        };

        var act = async () => await _sut.ConfigureCaCertificatesAsync(options).ConfigureAwait(true);

        await act.Should().NotThrowAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task ConfigureCaCertificatesAsync_NonExistentBundlePath_ShouldNotThrow()
    {
        var options = new CaCertificateOptions
        {
            UseSystemStore = false,
            CaBundlePath = "/nonexistent/ca-bundle.crt"
        };

        var act = async () => await _sut.ConfigureCaCertificatesAsync(options).ConfigureAwait(true);

        await act.Should().NotThrowAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task ConfigureCaCertificatesAsync_NonExistentAdditionalPaths_ShouldNotThrow()
    {
        var options = new CaCertificateOptions
        {
            UseSystemStore = false,
            AdditionalCaPaths = new List<string> { "/nonexistent/ca1.pem", "/nonexistent/ca2.pem" }
        };

        var act = async () => await _sut.ConfigureCaCertificatesAsync(options).ConfigureAwait(true);

        await act.Should().NotThrowAsync().ConfigureAwait(true);
    }

    [Fact]
    public void LoadCertificate_NullOrEmptyPath_ShouldThrowArgumentException()
    {
        var act1 = () => _sut.LoadCertificate(null!);
        var act2 = () => _sut.LoadCertificate("");
        var act3 = () => _sut.LoadCertificate("   ");

        act1.Should().Throw<ArgumentException>();
        act2.Should().Throw<ArgumentException>();
        act3.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void LoadCertificate_NonExistentPath_ShouldReturnNull()
    {
        var result = _sut.LoadCertificate("/nonexistent/cert.pem");

        result.Should().BeNull();
    }

    [Fact]
    public void LoadCertificate_InvalidCertificateData_ShouldReturnNull()
    {
        var tempPath = $"/test/invalid-cert-{Guid.NewGuid():N}.pem";
        _fs.WriteAllText(tempPath, "this is not a valid certificate");

        var result = _sut.LoadCertificate(tempPath);

        result.Should().BeNull();
    }

    [Fact]
    public void ValidateCertificate_NullCertificate_ShouldThrowArgumentNullException()
    {
        var act = () => _sut.ValidateCertificate(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task ConfigureCaCertificatesAsync_WithValidPemBundle_ShouldLoadCertificates()
    {
        var tempBundlePath = $"/test/ca-bundle-{Guid.NewGuid():N}.crt";
        var cert = CreateSelfSignedCert();
        var pem = $"-----BEGIN CERTIFICATE-----\n{Convert.ToBase64String(cert.Export(System.Security.Cryptography.X509Certificates.X509ContentType.Cert))}\n-----END CERTIFICATE-----";
        _fs.WriteAllText(tempBundlePath, pem);

        var options = new CaCertificateOptions
        {
            UseSystemStore = false,
            CaBundlePath = tempBundlePath
        };

        await _sut.ConfigureCaCertificatesAsync(options).ConfigureAwait(true);

        var loaded = _sut.LoadCertificate(tempBundlePath);
        loaded.Should().NotBeNull();
    }

    [Fact]
    public async Task ConfigureCaCertificatesAsync_WithAdditionalCaPaths_ShouldLoadExistingCerts()
    {
        var tempCertPath = $"/test/additional-ca-{Guid.NewGuid():N}.pem";
        var cert = CreateSelfSignedCert();
        _fs.WriteAllBytes(tempCertPath, cert.Export(System.Security.Cryptography.X509Certificates.X509ContentType.Cert));

        var options = new CaCertificateOptions
        {
            UseSystemStore = false,
            AdditionalCaPaths = new List<string> { tempCertPath }
        };

        await _sut.ConfigureCaCertificatesAsync(options).ConfigureAwait(true);

        var loaded = _sut.LoadCertificate(tempCertPath);
        loaded.Should().NotBeNull();
    }

    [Fact]
    public void ValidateCertificate_SelfSignedCert_WithLoadedCa_ShouldValidate()
    {
        var cert = CreateSelfSignedCert();
        var result = _sut.ValidateCertificate(cert);

        result.Should().BeTrue();
    }

    private static System.Security.Cryptography.X509Certificates.X509Certificate2 CreateSelfSignedCert()
    {
        using var rsa = System.Security.Cryptography.RSA.Create(2048);
        var req = new System.Security.Cryptography.X509Certificates.CertificateRequest(
            "CN=Test CA",
            rsa,
            System.Security.Cryptography.HashAlgorithmName.SHA256,
            System.Security.Cryptography.RSASignaturePadding.Pkcs1);

        req.CertificateExtensions.Add(
            new System.Security.Cryptography.X509Certificates.X509BasicConstraintsExtension(true, false, 0, true));

        return req.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(365));
    }
}
