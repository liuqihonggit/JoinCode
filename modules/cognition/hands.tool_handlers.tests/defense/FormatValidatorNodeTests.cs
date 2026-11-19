namespace Core.Tests;

public class FormatValidatorNodeTests {
    private readonly IFileSystem _fs = TestFileSystem.Current;

    [Fact]
    public async Task ValidateSettingsEditAsync_NonSettingsFile_ReturnsNull() {
        var node = new FormatValidatorNode(_fs);
        var filePath = CreateFile("{}");

        var result = await node.ValidateSettingsEditAsync(filePath, "a", "b", false, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public void TryGetKeywordSectionsEditViolation_NonKeywordFile_ReturnsFalse() {
        var node = new FormatValidatorNode(_fs);

        var hit = node.TryGetKeywordSectionsEditViolation("/regular/file.txt", out var violation);

        Assert.False(hit);
        Assert.Null(violation);
    }

    [Fact]
    public async Task TryGetKeywordSectionsEditViolation_KeywordFileNoAgent_ReturnsFalse() {
        var node = new FormatValidatorNode(_fs);
        var path = Path.Combine(Path.GetTempPath(), $"keyword-sections_{Guid.NewGuid():N}.json");
        await _fs.WriteAllText(path, "[]");

        var hit = node.TryGetKeywordSectionsEditViolation(path, out var violation);

        Assert.False(hit);
        Assert.Null(violation);
    }

    [Fact]
    public void TryGetDoctorAgentEditViolation_NoAgent_ReturnsFalse() {
        var node = new FormatValidatorNode(_fs);

        var hit = node.TryGetDoctorAgentEditViolation("/regular/file.txt", out var violation);

        Assert.False(hit);
        Assert.Null(violation);
    }

    [Fact]
    public void TryGetDoctorAgentEditViolation_DoctorAgentAllowedPath_ReturnsFalse() {
        var accessor = new Mock<ISubAgentContextAccessor>();
        accessor.SetupGet(a => a.Current).Returns(new SubAgentContext {
            AgentId = "test",
            Role = AgentRole.Executor,
            Task = "test",
            Variant = ExecutorVariant.Doctor
        });
        var node = new FormatValidatorNode(_fs, accessor.Object);

        var hit = node.TryGetDoctorAgentEditViolation("/home/.jcc/diag/file.txt", out var violation);

        Assert.False(hit);
        Assert.Null(violation);
    }

    [Fact]
    public void TryGetDoctorAgentEditViolation_DoctorAgentDisallowedPath_ReturnsViolation() {
        var accessor = new Mock<ISubAgentContextAccessor>();
        accessor.SetupGet(a => a.Current).Returns(new SubAgentContext {
            AgentId = "test",
            Role = AgentRole.Executor,
            Task = "test",
            Variant = ExecutorVariant.Doctor
        });
        var node = new FormatValidatorNode(_fs, accessor.Object);

        var hit = node.TryGetDoctorAgentEditViolation("/regular/file.txt", out var violation);

        Assert.True(hit);
        var actual = violation ?? throw new InvalidOperationException("doctor Agent 路径拒绝应返回违规消息");
        Assert.Contains("doctor Agent", actual);
    }

    private string CreateFile(string content) {
        var path = Path.Combine(Path.GetTempPath(), $"validator_test_{Guid.NewGuid():N}.txt");
        _fs.WriteAllText(path, content).GetAwaiter().GetResult();
        return path;
    }
}
