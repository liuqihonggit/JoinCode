namespace Guard.Tests.Security.Services;

public sealed class MtlsServiceTests
{
    private readonly IFileSystem _fs = TestFileSystem.Current;
    private readonly MtlsService _sut;

    public MtlsServiceTests()
    {
        _sut = new MtlsService(_fs, NullLogger<MtlsService>.Instance);
    }

    [Fact]
    public void IsMtlsConfigured_Initially_ShouldBeFalse()
    {
        _sut.IsMtlsConfigured.Should().BeFalse();
    }

    [Fact]
    public async Task ConfigureMtlsAsync_NullOptions_ShouldThrowArgumentNullException()
    {
        var act = async () => await _sut.ConfigureMtlsAsync(null!).ConfigureAwait(true);

        await act.Should().ThrowAsync<ArgumentNullException>().ConfigureAwait(true);
    }

    [Fact]
    public async Task ConfigureMtlsAsync_EmptyCertificatePath_ShouldReturnNotConfigured()
    {
        var options = new MtlsOptions
        {
            CertificatePath = "",
            CertificatePassword = null
        };

        var result = await _sut.ConfigureMtlsAsync(options).ConfigureAwait(true);

        result.IsConfigured.Should().BeFalse();
    }

    [Fact]
    public async Task ConfigureMtlsAsync_NonExistentCertificatePath_ShouldReturnNotConfigured()
    {
        var options = new MtlsOptions
        {
            CertificatePath = "/nonexistent/cert.pfx",
            CertificatePassword = null
        };

        var result = await _sut.ConfigureMtlsAsync(options).ConfigureAwait(true);

        result.IsConfigured.Should().BeFalse();
    }

    [Fact]
    public async Task ConfigureMtlsAsync_NonExistentCertificatePath_ShouldNotConfigureMtls()
    {
        var options = new MtlsOptions
        {
            CertificatePath = "/nonexistent/cert.pfx"
        };

        await _sut.ConfigureMtlsAsync(options).ConfigureAwait(true);

        _sut.IsMtlsConfigured.Should().BeFalse();
    }

    [Fact]
    public void CreateMtlsHandler_NullConfig_ShouldThrowArgumentNullException()
    {
        var act = () => _sut.CreateMtlsHandler(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void CreateMtlsHandler_NotConfigured_ShouldReturnDefaultHandler()
    {
        var config = new MtlsConfiguration { IsConfigured = false };

        var handler = _sut.CreateMtlsHandler(config);

        handler.Should().NotBeNull();
        handler.ClientCertificates.Count.Should().Be(0);
    }

    [Fact]
    public void CreateMtlsHandler_ConfiguredButNoCurrentConfig_ShouldReturnDefaultHandler()
    {
        var config = new MtlsConfiguration
        {
            IsConfigured = true,
            ClientCertificateThumbprint = "nonexistent-thumbprint"
        };

        var handler = _sut.CreateMtlsHandler(config);

        handler.Should().NotBeNull();
    }

    [Fact]
    public async Task ConfigureMtlsAsync_ValidCertificateFile_ShouldReturnConfigured()
    {
        var certPath = CreateTempSelfSignedCert();
        var options = new MtlsOptions
        {
            CertificatePath = certPath
        };

        var result = await _sut.ConfigureMtlsAsync(options).ConfigureAwait(true);

        result.IsConfigured.Should().BeTrue();
        result.ClientCertificateThumbprint.Should().NotBeNullOrEmpty();
        _sut.IsMtlsConfigured.Should().BeTrue();
    }

    [Fact]
    public async Task ConfigureMtlsAsync_WithCaCertificatePath_ShouldSetCaThumbprint()
    {
        var certPath = CreateTempSelfSignedCert();
        var options = new MtlsOptions
        {
            CertificatePath = certPath,
            CaCertificatePath = certPath
        };

        var result = await _sut.ConfigureMtlsAsync(options).ConfigureAwait(true);

        result.IsConfigured.Should().BeTrue();
        result.ServerCaThumbprint.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ConfigureMtlsAsync_WithNonExistentCaPath_ShouldStillConfigureWithoutCa()
    {
        var certPath = CreateTempSelfSignedCert();
        var options = new MtlsOptions
        {
            CertificatePath = certPath,
            CaCertificatePath = "/nonexistent/ca.pem"
        };

        var result = await _sut.ConfigureMtlsAsync(options).ConfigureAwait(true);

        result.IsConfigured.Should().BeTrue();
        result.ServerCaThumbprint.Should().BeNull();
    }

    [Fact]
    public async Task ConfigureMtlsAsync_InvalidCertData_ShouldReturnNotConfigured()
    {
        var tempPath = $"/test/invalid-cert-{Guid.NewGuid():N}.pfx";
        _fs.WriteAllBytes(tempPath, [0x00, 0x01, 0x02, 0x03]);

        var options = new MtlsOptions
        {
            CertificatePath = tempPath
        };

        var result = await _sut.ConfigureMtlsAsync(options).ConfigureAwait(true);

        result.IsConfigured.Should().BeFalse();
    }

    private string CreateTempSelfSignedCert()
    {
        using var rsa = System.Security.Cryptography.RSA.Create(2048);
        var req = new System.Security.Cryptography.X509Certificates.CertificateRequest(
            "CN=Test Client",
            rsa,
            System.Security.Cryptography.HashAlgorithmName.SHA256,
            System.Security.Cryptography.RSASignaturePadding.Pkcs1);

        var cert = req.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(365));

        var tempPath = $"/test/test-cert-{Guid.NewGuid():N}.cer";
        _fs.WriteAllBytes(tempPath, cert.Export(System.Security.Cryptography.X509Certificates.X509ContentType.Cert));
        return tempPath;
    }
}
