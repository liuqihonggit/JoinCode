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
    public void ValidateKeywordSectionsEdit_NonKeywordFile_ReturnsNull() {
        var node = new FormatValidatorNode(_fs);

        var result = node.ValidateKeywordSectionsEdit("/regular/file.txt");

        Assert.Null(result);
    }

    [Fact]
    public void ValidateKeywordSectionsEdit_KeywordFileNoAgent_ReturnsNull() {
        var node = new FormatValidatorNode(_fs);
        var path = Path.Combine(Path.GetTempPath(), $"keyword-sections_{Guid.NewGuid():N}.json");
        _fs.WriteAllText(path, "[]");

        var result = node.ValidateKeywordSectionsEdit(path);

        Assert.Null(result);
    }

    [Fact]
    public void ValidateDoctorAgentEdit_NoAgent_ReturnsNull() {
        var node = new FormatValidatorNode(_fs);

        var result = node.ValidateDoctorAgentEdit("/regular/file.txt");

        Assert.Null(result);
    }

    [Fact]
    public void ValidateDoctorAgentEdit_DoctorAgentAllowedPath_ReturnsNull() {
        var accessor = new Mock<ISubAgentContextAccessor>();
        accessor.SetupGet(a => a.Current).Returns(new SubAgentContext {
            AgentId = "test",
            Role = AgentRole.Executor,
            Task = "test",
            Variant = ExecutorVariant.Doctor
        });
        var node = new FormatValidatorNode(_fs, accessor.Object);

        var result = node.ValidateDoctorAgentEdit("/home/.jcc/diag/file.txt");

        Assert.Null(result);
    }

    [Fact]
    public void ValidateDoctorAgentEdit_DoctorAgentDisallowedPath_ReturnsError() {
        var accessor = new Mock<ISubAgentContextAccessor>();
        accessor.SetupGet(a => a.Current).Returns(new SubAgentContext {
            AgentId = "test",
            Role = AgentRole.Executor,
            Task = "test",
            Variant = ExecutorVariant.Doctor
        });
        var node = new FormatValidatorNode(_fs, accessor.Object);

        var result = node.ValidateDoctorAgentEdit("/regular/file.txt");

        Assert.NotNull(result);
        Assert.Contains("doctor Agent", result);
    }

    private string CreateFile(string content) {
        var path = Path.Combine(Path.GetTempPath(), $"validator_test_{Guid.NewGuid():N}.txt");
        _fs.WriteAllText(path, content);
        return path;
    }
}