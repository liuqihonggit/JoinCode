namespace Core.Tests;

public class SecretGuardNodeTests {
    [Fact]
    public void CheckSecrets_NullContent_ReturnsNull() {
        var node = new SecretGuardNode();
        var result = node.CheckSecrets("/test.txt", null);
        Assert.Null(result);
    }

    [Fact]
    public void CheckSecrets_NullGuard_ReturnsNull() {
        var node = new SecretGuardNode();
        var result = node.CheckSecrets("/test.txt", "some content");
        Assert.Null(result);
    }

    [Fact]
    public void CheckSecrets_GuardReturnsNull_PassesThrough() {
        var guard = new Mock<ITeamMemSecretGuard>();
        guard.Setup(g => g.CheckTeamMemSecrets(It.IsAny<string>(), It.IsAny<string>()))
             .Returns((string?)null);
        var node = new SecretGuardNode(guard.Object);

        var result = node.CheckSecrets("/test.txt", "safe content");

        Assert.Null(result);
    }

    [Fact]
    public void CheckSecrets_GuardReturnsError_ReturnsError() {
        var guard = new Mock<ITeamMemSecretGuard>();
        guard.Setup(g => g.CheckTeamMemSecrets(It.IsAny<string>(), It.IsAny<string>()))
             .Returns("detected secret: API_KEY=xxx");
        var node = new SecretGuardNode(guard.Object);

        var result = node.CheckSecrets("/test.txt", "API_KEY=xxx");

        Assert.Equal("detected secret: API_KEY=xxx", result);
    }

    [Fact]
    public void CheckSecrets_CallsGuardWithCorrectArgs() {
        var guard = new Mock<ITeamMemSecretGuard>();
        guard.Setup(g => g.CheckTeamMemSecrets("/path/file.txt", "content"))
             .Returns((string?)null)
             .Verifiable();
        var node = new SecretGuardNode(guard.Object);

        node.CheckSecrets("/path/file.txt", "content");

        guard.Verify();
    }
}