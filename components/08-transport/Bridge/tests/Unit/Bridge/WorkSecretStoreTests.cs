
namespace Bridge.Tests;

/// <summary>
/// WorkSecretStore 单元测试
/// 测试工作密钥的创建、加密存储、验证、轮换和撤销
/// </summary>
public sealed class WorkSecretStoreTests : IDisposable
{
    private readonly BridgeConfig _config = new() { EncryptionKeyBase64 = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)) };

    private WorkSecretStore CreateSut() =>
        new(_config, logger: null);

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task CreateAsync_ShouldCreateSecret()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = await sut.CreateAsync("api-key", "my-secret-value").ConfigureAwait(true);

        // Assert
        result.Should().NotBeNull();
        result.Name.Should().Be("api-key");
        result.SecretId.Should().NotBeNullOrEmpty();
        result.IsRevoked.Should().BeFalse();
        result.IsRotated.Should().BeFalse();
        result.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task CreateAsync_ShouldStoreEncryptedValue()
    {
        // Arrange
        var sut = CreateSut();
        var plainValue = "my-secret-value";

        // Act
        var result = await sut.CreateAsync("api-key", plainValue).ConfigureAwait(true);

        // Assert
        result.EncryptedValue.Should().NotBeNullOrEmpty();
        result.EncryptedValue.Should().NotBe(plainValue,
            "存储的值应为加密后的密文，而非明文");
    }

    [Fact]
    public async Task GetAsync_ShouldReturnSecret_WhenExists()
    {
        // Arrange
        var sut = CreateSut();
        var created = await sut.CreateAsync("api-key", "my-secret-value").ConfigureAwait(true);

        // Act
        var result = await sut.GetAsync(created.SecretId).ConfigureAwait(true);

        // Assert
        result.Should().NotBeNull();
        result!.SecretId.Should().Be(created.SecretId);
        result.Name.Should().Be("api-key");
    }

    [Fact]
    public async Task GetAsync_ShouldReturnNull_WhenNotExists()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = await sut.GetAsync("nonexistent-id").ConfigureAwait(true);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task ValidateAsync_ShouldReturnTrue_WhenValueMatches()
    {
        // Arrange
        var sut = CreateSut();
        var plainValue = "correct-secret-value";
        var created = await sut.CreateAsync("api-key", plainValue).ConfigureAwait(true);

        // Act
        var result = await sut.ValidateAsync(created.SecretId, plainValue).ConfigureAwait(true);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_ShouldReturnFalse_WhenValueDoesNotMatch()
    {
        // Arrange
        var sut = CreateSut();
        var created = await sut.CreateAsync("api-key", "correct-value").ConfigureAwait(true);

        // Act
        var result = await sut.ValidateAsync(created.SecretId, "wrong-value").ConfigureAwait(true);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_ShouldReturnFalse_WhenSecretIsRevoked()
    {
        // Arrange
        var sut = CreateSut();
        var plainValue = "secret-value";
        var created = await sut.CreateAsync("api-key", plainValue).ConfigureAwait(true);
        await sut.RevokeAsync(created.SecretId).ConfigureAwait(true);

        // Act
        var result = await sut.ValidateAsync(created.SecretId, plainValue).ConfigureAwait(true);

        // Assert
        result.Should().BeFalse("已撤销的密钥验证应失败");
    }

    [Fact]
    public async Task RotateAsync_ShouldCreateNewSecret_AndMarkOldAsRotated()
    {
        // Arrange
        var sut = CreateSut();
        var oldEntry = await sut.CreateAsync("api-key", "old-secret-value").ConfigureAwait(true);

        // Act
        var newEntry = await sut.RotateAsync(oldEntry.SecretId, "new-secret-value").ConfigureAwait(true);

        // Assert
        newEntry.Should().NotBeNull();
        newEntry.SecretId.Should().NotBe(oldEntry.SecretId,
            "轮换应生成新的 SecretId");
        newEntry.Name.Should().Be("api-key",
            "轮换后名称应保持一致");

        // 旧密钥应标记为已轮换
        var oldAfterRotation = await sut.GetAsync(oldEntry.SecretId).ConfigureAwait(true);
        oldAfterRotation.Should().NotBeNull();
        oldAfterRotation!.IsRotated.Should().BeTrue();
        oldAfterRotation.RotatedToSecretId.Should().Be(newEntry.SecretId);

        // 新密钥应可验证
        var isValid = await sut.ValidateAsync(newEntry.SecretId, "new-secret-value").ConfigureAwait(true);
        isValid.Should().BeTrue();
    }

    [Fact]
    public async Task RevokeAsync_ShouldMarkSecretAsRevoked()
    {
        // Arrange
        var sut = CreateSut();
        var created = await sut.CreateAsync("api-key", "secret-value").ConfigureAwait(true);

        // Act
        var result = await sut.RevokeAsync(created.SecretId).ConfigureAwait(true);

        // Assert
        result.Should().BeTrue();
        var stored = await sut.GetAsync(created.SecretId).ConfigureAwait(true);
        stored!.IsRevoked.Should().BeTrue();
    }
}
