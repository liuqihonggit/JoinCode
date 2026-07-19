namespace Core.Tests.State;

/// <summary>
/// DatabasePathResolver 单元测试 — 验证 SQLite DB 路径解析行为
/// 修复 P2-2: 默认路径应使用 AppDataFolder 而非 AppContext.BaseDirectory，
/// 避免 DB 落在 exe 同目录导致多用户/多测试共享（历史泄漏）
/// </summary>
public sealed class DatabasePathResolverTests : IDisposable
{
    private readonly AppDataPaths _originalPaths;

    public DatabasePathResolverTests()
    {
        // 备份原始 Paths，测试后恢复，避免影响其他测试
        _originalPaths = AppDataConstants.Paths;
    }

    public void Dispose()
    {
        AppDataConstants.Paths = _originalPaths;
    }

    /// <summary>
    /// 设置隔离的 AppDataFolder（绝对路径），确保测试可重现
    /// </summary>
    private static void SetTestAppDataFolder(string folder)
    {
        AppDataConstants.Paths = AppDataPaths.CreateForTest(appDataFolder: folder);
    }

    [Fact]
    public void Resolve_NullPath_ReturnsPathUnderAppDataFolder_NotBaseDirectory()
    {
        // Arrange — 用绝对路径避免依赖 %APPDATA%
        SetTestAppDataFolder(@"C:\TestAppData");

        // Act
        var result = DatabasePathResolver.Resolve(null);

        // Assert — 应该在 AppDataFolder 下，而非 AppContext.BaseDirectory
        Assert.Equal(Path.Combine(@"C:\TestAppData", "workflow_state.db"), result);
        Assert.DoesNotContain(AppContext.BaseDirectory, result);
    }

    [Fact]
    public void Resolve_EmptyPath_ReturnsPathUnderAppDataFolder()
    {
        // Arrange
        SetTestAppDataFolder(@"C:\TestAppData");

        // Act
        var result = DatabasePathResolver.Resolve("");

        // Assert
        Assert.Equal(Path.Combine(@"C:\TestAppData", "workflow_state.db"), result);
    }

    [Fact]
    public void Resolve_JsonExtension_ConvertsToDb_AndUsesAppDataFolder()
    {
        // Arrange
        SetTestAppDataFolder(@"C:\TestAppData");

        // Act
        var result = DatabasePathResolver.Resolve("workflow_state.json");

        // Assert — .json 应转换为 .db，且在 AppDataFolder 下
        Assert.Equal(Path.Combine(@"C:\TestAppData", "workflow_state.db"), result);
        Assert.EndsWith(".db", result);
    }

    [Fact]
    public void Resolve_RelativePath_CombinesWithAppDataFolder_NotBaseDirectory()
    {
        // Arrange
        SetTestAppDataFolder(@"C:\TestAppData");

        // Act
        var result = DatabasePathResolver.Resolve("subdir/state.db");

        // Assert — 相对路径应与 AppDataFolder 组合
        Assert.Equal(Path.Combine(@"C:\TestAppData", "subdir/state.db"), result);
        Assert.DoesNotContain(AppContext.BaseDirectory, result);
    }

    [Fact]
    public void Resolve_AbsolutePath_ReturnsAsIs()
    {
        // Arrange
        SetTestAppDataFolder(@"C:\TestAppData");
        var absolutePath = @"D:\Custom\state.db";

        // Act
        var result = DatabasePathResolver.Resolve(absolutePath);

        // Assert — 绝对路径应原样返回
        Assert.Equal(absolutePath, result);
    }

    [Fact]
    public void Resolve_CustomDefaultFileName_UsesAppDataFolder()
    {
        // Arrange
        SetTestAppDataFolder(@"C:\TestAppData");

        // Act
        var result = DatabasePathResolver.Resolve(null, "custom_state.db");

        // Assert — 自定义默认文件名也应放在 AppDataFolder 下
        Assert.Equal(Path.Combine(@"C:\TestAppData", "custom_state.db"), result);
    }

    [Fact]
    public void Resolve_RelativeAppDataFolder_RootsToApplicationData()
    {
        // Arrange — 相对路径 AppDataFolder 应 root 到 SpecialFolder.ApplicationData
        SetTestAppDataFolder("TestJcc");

        // Act
        var result = DatabasePathResolver.Resolve(null);

        // Assert — 应该在 %APPDATA%/TestJcc 下
        var expectedBase = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "TestJcc");
        Assert.Equal(Path.Combine(expectedBase, "workflow_state.db"), result);
        Assert.DoesNotContain(AppContext.BaseDirectory, result);
    }
}
