namespace Guard.Security.Tests;

/// <summary>
/// bash 命令静态识别覆盖面对照。
/// <para>
/// bash 执行链上真正生效的是 <see cref="ICommandDangerClassifier"/>（`ShellCommand.Parse` 切首词 →
/// `DangerousCommandCatalog` 查表）；`guard.security/services/bash_validation` 那套
/// tree-sitter 全树遍历的 <see cref="IBashSecurityValidator"/> 在 modules 下**没有生产消费者**。
/// 本测试用同一批复合 / 混淆 / 间接调用命令跑两条路径，把差异摊开：
/// 「分类器放行、AST 校验器拒绝」的样本，就是当前执行链的覆盖缺口面。
/// </para>
/// <para>
/// 全部用例只做静态判定，**不执行**任何命令。
/// </para>
/// </summary>
public class BashCommandCoverageTests {
    private readonly ITestOutputHelper _output;

    /// <summary>构造并注入测试输出</summary>
    public BashCommandCoverageTests(ITestOutputHelper output) {
        _output = output;
    }

    /// <summary>
    /// 复合命令（分号 / 与或 / 管道 / 换行）—— 分类器只看第一个词，后续子命令是否还被检查？
    /// </summary>
    [Fact]
    public async Task CompoundCommands_ClassifierSeesOnlyFirstToken() {
        string[] cases = [
            "echo hi; rm -rf ./build",
            "true && rm -rf ./build",
            "false || rm -rf ./build",
            "echo hi | xargs rm -rf",
            "echo hi\nrm -rf ./build",
        ];

        await DumpComparisonAsync("复合命令", cases);
    }

    /// <summary>
    /// 混淆写法 —— 引号插入、大小写、额外选项，看两条路径各自能否剥掉噪声后认出命令。
    /// </summary>
    [Fact]
    public async Task ObfuscatedCommands_ClassifierVersusAst() {
        string[] cases = [
            "r''m -rf ./build",
            "rm -r'f' ./build",
            "\"rm\" -rf ./build",
            "RM -RF ./build",
            "rm -rf --preserve-root ./build",
        ];

        await DumpComparisonAsync("混淆写法", cases);
    }

    /// <summary>
    /// 间接调用 —— 解释器 / jq system / heredoc，命令名本身看起来无害。
    /// </summary>
    [Fact]
    public async Task IndirectInvocation_ClassifierVersusAst() {
        string[] cases = [
            "bash -c 'rm -rf ./build'",
            "python -c \"import os; os.system('id')\"",
            "jq -n 'system(\"id\")'",
            "cat > /tmp/x.sh <<EOF\nrm -rf ./build\nEOF",
            "eval \"$(echo cm0gLXJmIC4vYnVpbGQ= | base64 -d)\"",
        ];

        await DumpComparisonAsync("间接调用", cases);
    }

    /// <summary>
    /// 对照组：确认分级口径，以及两条必须放行的良性命令。
    /// <para>
    /// 注意 `rm -rf ./build` 是 <c>Execution</c>（红灯 ask）而不是 <c>Dangerous</c>（黑灯直接拒）——
    /// `Dangerous` 留给整盘/系统级操作。这个区别是本次调查的关键：
    /// Execution 在 Auto 模式会被「拒绝并引导切 Ask」，而 Safe 在所有模式下直接放行，
    /// 所以「复合命令被判 Safe」才是真降级。
    /// </para>
    /// </summary>
    [Fact]
    public async Task Baseline_LevelSemanticsAndNoFalsePositive() {
        await using var classifier = new CommandDangerClassifier();

        classifier.Classify("rm -rf ./build").Level.Should().Be(
            CommandDangerLevel.Execution,
            "相对路径删除是红灯 ask，不是黑灯");
        classifier.Classify("rm -rf /").Level.Should().Be(
            CommandDangerLevel.Dangerous,
            "整盘删除必须是黑灯直接拒绝");
        classifier.Classify("git status").Level.Should().NotBe(CommandDangerLevel.Dangerous);
        classifier.Classify("echo \"rm -rf /\"").Level.Should().NotBe(
            CommandDangerLevel.Dangerous,
            "引号内的字符串没有执行语义，不应被判 Dangerous");
    }

    /// <summary>
    /// 复合命令的等级必须等于「最严的那个子命令」—— 这是修复后的回归锁。
    /// </summary>
    [Fact]
    public async Task CompoundCommand_LevelMustNotDropBelowItsWorstSegment() {
        await using var classifier = new CommandDangerClassifier();

        var alone = classifier.Classify("rm -rf ./build").Level;
        string[] prefixed = [
            "echo hi; rm -rf ./build",
            "true && rm -rf ./build",
            "false || rm -rf ./build",
            "echo hi\nrm -rf ./build",
            // 管道前缀与分号前缀是同一类降级：首词是 echo，真正删文件的是管道右侧
            "echo hi | xargs rm -rf",
            "printf './build\n' | xargs rm -rf",
        ];

        foreach (var command in prefixed) {
            classifier.Classify(command).Level.Should().Be(
                alone,
                $"给危险子命令加前缀不改变等级：'{command.Replace("\n", "\\n", StringComparison.Ordinal)}'");
        }
    }

    private async Task DumpComparisonAsync(string title, string[] cases) {
        await using var classifier = new CommandDangerClassifier();
        using var walker = new BashAstSecurityWalker();
        await using var validator = new BashSecurityValidator(walker);

        _output.WriteLine($"── {title} ──");
        _output.WriteLine($"{"命令",-52} {"分类器",-12} {"AST 校验",-22} 缺口");

        foreach (var command in cases) {
            var level = classifier.Classify(command).Level.ToString();
            var ast = validator.Validate(command);
            var astText = ast.IsSafe
                ? "safe"
                : $"{ast.CheckId?.ToString() ?? "unsafe"}({ast.Message})";

            var classifierPassed = level is nameof(CommandDangerLevel.Safe) or nameof(CommandDangerLevel.Unknown);
            var gap = classifierPassed && !ast.IsSafe ? "  ← 仅 AST 能识别" : "";
            var display = command.Replace("\n", "\\n", StringComparison.Ordinal);
            _output.WriteLine($"{display,-52} {level,-12} {astText,-22}{gap}");
        }

        _output.WriteLine("");
    }

    /// <summary>
    /// 反向保护：良性子命令拼接不应被推高等级（拆段不能变成「见分隔符就升级」）。
    /// </summary>
    [Fact]
    public async Task CompoundCommand_BenignSegmentsKeepTheirLevel() {
        await using var classifier = new CommandDangerClassifier();

        var gitStatus = classifier.Classify("git status").Level;
        classifier.Classify("git status && git diff").Level.Should().Be(
            gitStatus,
            "两条只读 git 子命令拼接，等级应等于单条");

        var ls = classifier.Classify("ls -la").Level;
        classifier.Classify("ls -la; pwd").Level.Should().Be(
            ls,
            "两条无害命令拼接不应被推高");
    }
}
