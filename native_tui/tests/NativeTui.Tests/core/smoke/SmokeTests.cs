namespace NativeTui.Tests.Core.Smoke;

/// <summary>
/// 工程骨架冒烟测试：验证独立工程的核心架构约束（ADR 0140）。
/// </summary>
public static class SmokeTests
{
    /// <summary>
    /// 主仓库程序集前缀黑名单 — native_tui 必须零 modules/libs 依赖，
    /// 出现任何前缀匹配即说明构建配置遮蔽失效（根 Directory.Build.props 泄漏）。
    /// </summary>
    private static readonly string[] s_bannedAssemblyPrefixes =
    [
        "Structura",
        "JoinCode",
        "Abstractions",
        "Composition",
    ];

    [Fact]
    public static void LoadedAssemblies_ContainNoMainRepoModules()
    {
        var loaded = AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetName().Name ?? string.Empty)
            .ToList();
        var violated = loaded.Where(name => s_bannedAssemblyPrefixes.Any(name.StartsWith)).ToList();
        Assert.True(violated.Count == 0,
            $"检测到主仓库程序集泄漏: [{string.Join(", ", violated)}] — " +
            "native_tui 必须零 modules 依赖，请检查 native_tui/Directory.Build.props 是否正确遮蔽根部配置");
    }

    [Fact]
    public static void CoreLibrary_IsReferencedAndLoadable()
    {
        var coreAssembly = typeof(NativeTui.Core.Marker).Assembly;
        Assert.Equal("NativeTui.Core", coreAssembly.GetName().Name);
    }
}
