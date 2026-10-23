namespace NativeTui.Tests.Repl;

/// <summary>
/// Bug3 回归：TUI 用户级配置默认路径必须跟随 JCC_APP_DATA_FOLDER 覆盖 —
/// 与 modules/abstractions AppDataPaths.JccDirectory 同一契约（测试隔离场景 TUI 不得读写真实 ~/.jcc/）。
/// 环境变量测试集中本类（xUnit 同类内串行），避免与并行测试类交叉污染。
/// </summary>
public sealed class TuiConfigPathOverrideTests
{
    private const string EnvVar = "JCC_APP_DATA_FOLDER";

    /// <summary>绝对路径覆盖：keybindings.json 跟随覆盖目录（原硬编码 ~/.jcc/ 为红）。</summary>
    [Fact]
    public void KeybindingLoaderDefaultPath_FollowsAbsoluteOverride()
    {
        var dir = Path.Combine(Path.GetTempPath(), "jcc_tui_red_" + Guid.NewGuid().ToString("N")[..8]);
        using var scope = new EnvScope(EnvVar, dir);
        Assert.Equal(Path.Combine(dir, "keybindings.json"), KeybindingLoader.DefaultPath);
    }

    /// <summary>绝对路径覆盖：command-history.json 跟随覆盖目录（原硬编码 ~/.jcc/ 为红）。</summary>
    [Fact]
    public void CommandUsageDefaultFilePath_FollowsAbsoluteOverride()
    {
        var dir = Path.Combine(Path.GetTempPath(), "jcc_tui_red_" + Guid.NewGuid().ToString("N")[..8]);
        using var scope = new EnvScope(EnvVar, dir);
        Assert.Equal(Path.Combine(dir, "command-history.json"), CommandUsageMemory.DefaultFilePath);
    }

    /// <summary>默认（未覆盖）：根目录为 UserProfile/.jcc — 与 AppDataPaths.JccDirectory 语义一致。</summary>
    [Fact]
    public void JccHomeRoot_DefaultsToUserProfileDotJcc()
    {
        using var scope = new EnvScope(EnvVar, null);
        Assert.Equal(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".jcc"),
            JccHome.RootDirectory);
    }

    /// <summary>相对名覆盖：拼 UserProfile — 与 AppDataPaths.JccDirectory 语义一致。</summary>
    [Fact]
    public void JccHomeRoot_RelativeNameCombinesUserProfile()
    {
        using var scope = new EnvScope(EnvVar, ".jcc-tui");
        Assert.Equal(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".jcc-tui"),
            JccHome.RootDirectory);
    }

    /// <summary>Combine：文件名拼接在根目录下。</summary>
    [Fact]
    public void JccHomeCombine_AppendsFileNameToRoot()
    {
        var dir = Path.Combine(Path.GetTempPath(), "jcc_tui_red_" + Guid.NewGuid().ToString("N")[..8]);
        using var scope = new EnvScope(EnvVar, dir);
        Assert.Equal(Path.Combine(dir, "any.json"), JccHome.Combine("any.json"));
    }

    /// <summary>环境变量 scope — 设置覆盖值并在 Dispose 还原原始值（null 表示未设置）。</summary>
    private sealed class EnvScope : IDisposable
    {
        private readonly string _var;
        private readonly string? _original;

        public EnvScope(string var, string? value)
        {
            _var = var;
            _original = Environment.GetEnvironmentVariable(var);
            Environment.SetEnvironmentVariable(var, value);
        }

        public void Dispose() => Environment.SetEnvironmentVariable(_var, _original);
    }
}
