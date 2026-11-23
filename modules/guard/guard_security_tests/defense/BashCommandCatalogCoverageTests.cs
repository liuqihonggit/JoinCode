namespace Guard.Security.Tests;

/// <summary>
/// 危险分级的两条「口径一致性」回归锁。
/// <para>
/// 一、**参数含义依赖命令**。<c>-r</c> 在 <c>rm</c> 上是递归删除，在 <c>sort</c> 上是逆序、在 <c>ls</c> 上是
/// 递归列目录。参数表若全局生效，<c>ls -R</c>、<c>tail -f</c>、<c>grep -r foo .</c>、
/// <c>Get-ChildItem -Recurse</c> 全部被判 <c>Execution</c>，而 <c>Execution</c> 在 Auto 模式下会被拒 ——
/// 日常只读命令反而成了被拦的那一批。
/// </para>
/// <para>
/// 二、**登记覆盖要跟得上使用**。catalog 未登记的命令是 <c>Unknown</c>（黄灯 ask），Auto 模式下同样被拒。
/// 缺 <c>find</c>、<c>sort</c>、<c>jq</c> 这些每天都在用的命令，等于把 Auto 模式变成「什么都不能跑」。
/// 但登记为 <c>Safe</c> 的前提是把它能执行命令的那些形式单独升级，否则登记 = 开洞。
/// </para>
/// </summary>
public class BashCommandCatalogCoverageTests {
    private readonly ITestOutputHelper _output;

    /// <summary>构造并注入测试输出</summary>
    public BashCommandCatalogCoverageTests(ITestOutputHelper output) {
        _output = output;
    }

    /// <summary>只读命令上的 -r / -R / -f / -Recurse 不能被当成递归/强制删除。</summary>
    [Theory]
    [InlineData("ls -R")]
    [InlineData("ls -la -r")]
    [InlineData("tail -f log.txt")]
    [InlineData("sort -r file.txt")]
    [InlineData("grep -r foo .")]
    [InlineData("grep -f pats.txt file.txt")]
    [InlineData("Get-ChildItem -Recurse")]
    [InlineData("find . -name \"*.cs\" -type f")]
    public async Task ReadOnlyCommand_WithRecursiveOrForceFlag_StaysSafe(string command) {
        await using var classifier = new CommandDangerClassifier();

        var level = classifier.Classify(command).Level;
        _output.WriteLine($"{command,-42} -> {level}");

        level.Should().Be(CommandDangerLevel.Safe, $"只读命令的 -r/-f 没有破坏性含义：'{command}'");
    }

    /// <summary>同一个参数在删除类命令上仍然要判 Execution —— 收窄作用域不能把防线一起收掉。</summary>
    [Theory]
    [InlineData("rm -r ./build")]
    [InlineData("rm -f ./build")]
    [InlineData("rm -rf ./build")]
    [InlineData("rm -R ./build")]
    [InlineData("del /s output")]
    // ForceOperation 在复制/移动上同样成立
    [InlineData("mv -f a.txt b.txt")]
    public async Task DestructiveCommand_WithSameFlag_StaysExecution(string command) {
        await using var classifier = new CommandDangerClassifier();

        classifier.Classify(command).Level.Should().Be(
            CommandDangerLevel.Execution,
            $"删除/移动类命令上的 -r/-f 必须仍是红灯：'{command}'");
    }

    /// <summary>日常只读与数据处理命令必须已登记，不然 Auto 模式会把它们当未知命令拒掉。</summary>
    [Theory]
    [InlineData("find . -name \"*.cs\"")]
    [InlineData("rg TODO src/")]
    [InlineData("sort -k2 file.txt")]
    [InlineData("uniq -c file.txt")]
    [InlineData("cut -d, -f1 file.csv")]
    [InlineData("tr a-z A-Z")]
    [InlineData("diff a.txt b.txt")]
    [InlineData("stat file.txt")]
    [InlineData("tree -L 2")]
    [InlineData("jq .items data.json")]
    [InlineData("awk '{print $1}' file.txt")]
    [InlineData("sed 's/a/b/' file.txt")]
    public async Task CommonReadOnlyCommands_AreRegistered(string command) {
        await using var classifier = new CommandDangerClassifier();

        var level = classifier.Classify(command).Level;
        _output.WriteLine($"{command,-42} -> {level}");

        level.Should().NotBe(CommandDangerLevel.Unknown, $"常用命令不该是「未登记」：'{command}'");
        level.Should().Be(CommandDangerLevel.Safe, $"只读命令应为白灯：'{command}'");
    }

    /// <summary>
    /// 登记为 Safe 的代价必须由组合检测补回来 —— 这些形式能执行命令或批量删除。
    /// </summary>
    [Theory]
    [InlineData("find . -name \"*.tmp\" -delete", CommandDangerLevel.Execution)]
    [InlineData("find . -name \"*.tmp\" -exec rm {} \\;", CommandDangerLevel.Execution)]
    [InlineData("awk 'BEGIN { system(\"id\") }'", CommandDangerLevel.Execution)]
    [InlineData("jq -n 'system(\"id\")'", CommandDangerLevel.Execution)]
    [InlineData("sed -i 's/a/b/' file.txt", CommandDangerLevel.LightValidation)]
    public async Task RegisteredCommand_DangerousForm_IsUpgraded(string command, CommandDangerLevel expected) {
        await using var classifier = new CommandDangerClassifier();

        var level = classifier.Classify(command).Level;
        _output.WriteLine($"{command,-46} -> {level}");

        level.Should().Be(expected, $"登记 Safe 之后，可执行/可写的形式必须单独升级：'{command}'");
    }

    /// <summary>包装器不改语义，真实命令在它的参数里 —— 不能因为首词是 env/nohup/time 就放行。</summary>
    [Theory]
    [InlineData("env rm -rf /", CommandDangerLevel.Dangerous)]
    [InlineData("nohup rm -rf ./build", CommandDangerLevel.Execution)]
    [InlineData("time rm -rf ./build", CommandDangerLevel.Execution)]
    [InlineData("env FOO=1 rm -rf ./build", CommandDangerLevel.Execution)]
    public async Task Wrapper_IsTransparent_ForDangerLevel(string command, CommandDangerLevel expected) {
        await using var classifier = new CommandDangerClassifier();

        classifier.Classify(command).Level.Should().Be(expected, $"包装器不应盖住真实命令：'{command}'");
    }

    /// <summary>包装器后面没有命令时按它自己判（`env` 打印环境变量是只读）。</summary>
    [Theory]
    [InlineData("env")]
    [InlineData("nohup ls -la")]
    [InlineData("time ls -la")]
    public async Task Wrapper_WithoutDangerousPayload_StaysSafe(string command) {
        await using var classifier = new CommandDangerClassifier();

        classifier.Classify(command).Level.Should().Be(CommandDangerLevel.Safe, $"无危险载荷的包装器应放行：'{command}'");
    }
}
