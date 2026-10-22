namespace MockServer.E2E.Tests;

/// <summary>
/// P2 隔离写命令 E2E 覆盖测试 — TASK065 第六波补覆盖
/// 包含: /reset-config all（JCC_APP_DATA_FOLDER 隔离目录重置）、/code-index（fixture 目录索引重建）
/// 隔离验证: /reset-config 写入目标为 runner 设置的 %TEMP%\jcc_test_{guid}（Path.Combine 对绝对路径直接采用），不碰真实 ~/.jcc
/// </summary>
public sealed class ChatCommandP2IsolatedCoverageTests : CoverageTestBase {
    public ChatCommandP2IsolatedCoverageTests(ITestOutputHelper output) : base(output) { }

    /// <summary>/reset-config all — 断言隔离 AppData 配置重置输出（重置开始/配置树骨架/重置完成）且无错误</summary>
    [Fact]
    public async Task ResetConfigAllCommand_ShouldResetIsolatedAppData() {
        await RunScriptAsync(ChatCommandConversationScripts.ResetConfigAllCommand).ConfigureAwait(true);
    }

    /// <summary>/code-index — 断言 fixture 目录索引构建完成（构建开始/完成提示/统计输出）且无错误</summary>
    [Fact]
    public async Task CodeIndexCommand_ShouldBuildIndexInFixtureDir() {
        var fixtureDir = await CreateCodeIndexFixtureAsync().ConfigureAwait(true);
        ChatCommandConversationScripts.CodeIndexFixtureDirectory = fixtureDir;
        try {
            await RunScriptAsync(ChatCommandConversationScripts.CodeIndexCommand).ConfigureAwait(true);
        } finally {
            ChatCommandConversationScripts.CodeIndexFixtureDirectory = null;
            CleanupCodeIndexFixture(fixtureDir);
        }
    }

    /// <summary>
    /// 创建 /code-index fixture 目录 — %TEMP%\jcc_e2e_codeindex_{guid} + 2 个小 .cs 文件
    /// 小目录控制 RebuildIndexAsync 耗时（Ast 集成测试 WorkingDirectory 指向 fixture 的先例）
    /// </summary>
    /// <returns>fixture 目录绝对路径</returns>
    private static async Task<string> CreateCodeIndexFixtureAsync() {
        var fs = TestConfiguration.FileSystem;
        var fixtureDir = fs.CombinePath(Path.GetTempPath(), $"jcc_e2e_codeindex_{Guid.NewGuid():N}");
        fs.CreateDirectory(fixtureDir);
        await fs.WriteAllTextAsync(fs.CombinePath(fixtureDir, "Calculator.cs"), CalculatorFixtureSource).ConfigureAwait(true);
        await fs.WriteAllTextAsync(fs.CombinePath(fixtureDir, "Greeter.cs"), GreeterFixtureSource).ConfigureAwait(true);
        return fixtureDir;
    }

    /// <summary>
    /// 清理 fixture 目录（含索引产物 {fixture}/.jcc/code-index/）— %TEMP% 下测试自建产物
    /// 清理失败不阻断测试结果（%TEMP% 残留无害，同 DualRoleConversationRunner 清理临时目录先例）
    /// </summary>
    /// <param name="fixtureDir">fixture 目录绝对路径</param>
    private void CleanupCodeIndexFixture(string fixtureDir) {
        try {
            var fs = TestConfiguration.FileSystem;
            if (fs.DirectoryExists(fixtureDir)) {
                fs.DeleteDirectory(fixtureDir, recursive: true);
            }
        } catch (Exception ex) {
            Output.WriteLine($"[P2] ⚠ fixture 清理失败（%TEMP% 残留无害）: {ex.Message}");
        }
    }

    /// <summary>fixture 源码 1 — 计算器（含类/方法符号，供索引器解析）</summary>
    private const string CalculatorFixtureSource = """
        namespace CodeIndexFixture;

        /// <summary>fixture 计算器 — /code-index E2E 索引目标</summary>
        public static class Calculator {
            /// <summary>两数相加</summary>
            public static int Add(int a, int b) => a + b;

            /// <summary>两数相乘</summary>
            public static int Multiply(int a, int b) => a * b;
        }
        """;

    /// <summary>fixture 源码 2 — 问候器（含类/方法符号，供索引器解析）</summary>
    private const string GreeterFixtureSource = """
        namespace CodeIndexFixture;

        /// <summary>fixture 问候器 — /code-index E2E 索引目标</summary>
        public static class Greeter {
            /// <summary>生成问候语</summary>
            public static string Greet(string name) => $"Hello, {name}!";
        }
        """;
}
