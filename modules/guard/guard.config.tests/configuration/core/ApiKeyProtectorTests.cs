namespace Guard.Tests.Configuration;

/// <summary>
/// C6: ApiKeyProtector 测试 — 验证 DPAPI 加密/解密 roundtrip + 明文不泄露 + 旧明文兼容
/// </summary>
public class ApiKeyProtectorTest {
    [Fact]
    public void Encrypt_Plaintext_ReturnsDpapiPrefix() {
        var encrypted = ApiKeyProtector.Encrypt("sk-test-key-12345");
        encrypted.Should().StartWith("dpapi:");
    }

    [Fact]
    public void Encrypt_Plaintext_DoesNotContainPlaintext() {
        const string plaintext = "sk-secret-key-abc123";
        var encrypted = ApiKeyProtector.Encrypt(plaintext);
        encrypted.Should().NotContain(plaintext);
    }

    [Fact]
    public void Decrypt_EncryptedValue_ReturnsOriginal() {
        const string plaintext = "sk-roundtrip-test-key";
        var encrypted = ApiKeyProtector.Encrypt(plaintext);
        var decrypted = ApiKeyProtector.Decrypt(encrypted);
        decrypted.Should().Be(plaintext);
    }

    [Fact]
    public void Decrypt_PlaintextWithoutPrefix_ReturnsAsIs() {
        const string plaintext = "sk-legacy-plaintext-key";
        var result = ApiKeyProtector.Decrypt(plaintext);
        result.Should().Be(plaintext);
    }

    [Fact]
    public void Decrypt_EmptyString_ReturnsEmpty() {
        ApiKeyProtector.Decrypt("").Should().Be("");
    }

    [Fact]
    public void IsSupported_OnWindows_ReturnsTrue() {
        ApiKeyProtector.IsSupported.Should().Be(OperatingSystem.IsWindows());
    }

    [Fact]
    public async Task SaveApiKeyToJccAsync_EncryptsKey_WithDpapiPrefix() {
        await using var fs = new InMemoryFileSystem();
        var authPath = AppDataConstants.Paths.AuthFilePath;
        var dir = Path.GetDirectoryName(authPath);
        if (!string.IsNullOrEmpty(dir))
            fs.CreateDirectory(dir);

        await ConfigLoader.SaveApiKeyToJccAsync("openai", "sk-test-secret-key", fs);

        var content = await fs.ReadAllTextAsync(authPath);
        content.Should().Contain("dpapi:");
        content.Should().NotContain("sk-test-secret-key");
    }
}
