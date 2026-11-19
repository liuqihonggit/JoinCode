namespace Core.Tests;

public class SecretGuardNodeTests {
    private delegate bool TryGetTeamMemSecretViolationCallback(string filePath, string content, out string? violation);

    private static Mock<ITeamMemSecretGuard> CreateGuard(bool hit, string? violation) {
        var guard = new Mock<ITeamMemSecretGuard>();
        guard.Setup(g => g.TryGetTeamMemSecretViolation(It.IsAny<string>(), It.IsAny<string>(), out It.Ref<string?>.IsAny))
             .Returns(new TryGetTeamMemSecretViolationCallback((string _, string _, out string? v) => {
                 v = violation;
                 return hit;
             }));
        return guard;
    }

    [Fact]
    public void CheckSecrets_NullContent_ReturnsFalse() {
        var node = new SecretGuardNode();
        var hit = node.TryGetSecretViolation("/test.txt", null, out var result);
        Assert.False(hit);
        Assert.Null(result);
    }

    [Fact]
    public void CheckSecrets_NullGuard_ReturnsFalse() {
        var node = new SecretGuardNode();
        var hit = node.TryGetSecretViolation("/test.txt", "some content", out var result);
        Assert.False(hit);
        Assert.Null(result);
    }

    [Fact]
    public void CheckSecrets_GuardReturnsNull_PassesThrough() {
        var node = new SecretGuardNode(CreateGuard(false, null).Object);

        var hit = node.TryGetSecretViolation("/test.txt", "safe content", out var result);

        Assert.False(hit);
        Assert.Null(result);
    }

    [Fact]
    public void CheckSecrets_GuardReturnsError_ReturnsError() {
        var node = new SecretGuardNode(CreateGuard(true, "detected secret: API_KEY=xxx").Object);

        var hit = node.TryGetSecretViolation("/test.txt", "API_KEY=xxx", out var result);

        Assert.True(hit);
        Assert.Equal("detected secret: API_KEY=xxx", result);
    }

    [Fact]
    public void CheckSecrets_CallsGuardWithCorrectArgs() {
        var guard = new Mock<ITeamMemSecretGuard>();
        guard.Setup(g => g.TryGetTeamMemSecretViolation("/path/file.txt", "content", out It.Ref<string?>.IsAny))
             .Returns(new TryGetTeamMemSecretViolationCallback((string _, string _, out string? v) => {
                 v = null;
                 return false;
             }))
             .Verifiable();
        var node = new SecretGuardNode(guard.Object);

        node.TryGetSecretViolation("/path/file.txt", "content", out _);

        guard.Verify();
    }
}
