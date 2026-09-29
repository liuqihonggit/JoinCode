namespace Hands.Tests.Shell;

/// <summary>
/// SedValidator 单元测试 — 验证 sed 命令约束检查、行打印命令识别、替换命令分类、
/// 危险操作黑名单、表达式提取、未转义 / 计数。
/// 对齐 TS sedValidation.ts,纯计算确定性测试。
/// </summary>
public class SedValidatorTest {
    // ===== CheckSedConstraints 公共约束检查 =====

    [Fact]
    public void CheckSedConstraints_EmptyCommand_ReturnsPassthrough() {
        var result = SedValidator.CheckSedConstraints("");
        result.Behavior.Should().Be(PermissionBehavior.Passthrough);
    }

    [Fact]
    public void CheckSedConstraints_WhitespaceCommand_ReturnsPassthrough() {
        var result = SedValidator.CheckSedConstraints("   ");
        result.Behavior.Should().Be(PermissionBehavior.Passthrough);
    }

    [Fact]
    public void CheckSedConstraints_NonSedCommand_ReturnsPassthrough() {
        var result = SedValidator.CheckSedConstraints("echo hello");
        result.Behavior.Should().Be(PermissionBehavior.Passthrough);
    }

    [Fact]
    public void CheckSedConstraints_LinePrintingCommand_ReturnsPassthrough() {
        var result = SedValidator.CheckSedConstraints("sed -n '1p'");
        result.Behavior.Should().Be(PermissionBehavior.Passthrough);
    }

    [Fact]
    public void CheckSedConstraints_StandardSubstitution_ReturnsPassthrough() {
        var result = SedValidator.CheckSedConstraints("sed 's/a/b/'");
        result.Behavior.Should().Be(PermissionBehavior.Passthrough);
    }

    [Fact]
    public void CheckSedConstraints_InPlaceWithoutAllow_ReturnsAsk() {
        var result = SedValidator.CheckSedConstraints("sed -i 's/a/b/'", allowFileWrites: false);
        result.Behavior.Should().Be(PermissionBehavior.Ask);
        result.Message.Should().Contain("确认");
    }

    [Fact]
    public void CheckSedConstraints_InPlaceWithAllow_ReturnsPassthrough() {
        var result = SedValidator.CheckSedConstraints("sed -i 's/a/b/'", allowFileWrites: true);
        result.Behavior.Should().Be(PermissionBehavior.Passthrough);
    }

    [Fact]
    public void CheckSedConstraints_DangerousBraceInPattern_ReturnsDeny() {
        // { 在 pattern 中,substResult 为 Passthrough,但 ContainsDangerousOperations 触发
        var result = SedValidator.CheckSedConstraints("sed 's/{a/b/'");
        result.Behavior.Should().Be(PermissionBehavior.Deny);
        result.Message.Should().Contain("危险操作");
    }

    [Fact]
    public void CheckSedConstraints_UnknownFlag_ReturnsDeny() {
        var result = SedValidator.CheckSedConstraints("sed -x 's/a/b/'");
        result.Behavior.Should().Be(PermissionBehavior.Deny);
    }

    // ===== IsLinePrintingCommand 行打印命令识别 =====

    [Theory]
    [InlineData("sed -n '1p'", true)]
    [InlineData("sed -n '1,5p'", true)]
    [InlineData("sed -n '1p;2p'", true)]
    [InlineData("sed --quiet '3p'", true)]
    [InlineData("sed --silent '3p'", true)]
    [InlineData("sed '1p'", false)]          // 无 -n 标志
    [InlineData("sed -n 's/a/b/'", false)]   // 非 Np 格式
    [InlineData("sed -x '1p'", false)]       // 不认识标志
    [InlineData("sed -n", false)]            // 无表达式
    [InlineData("sed -n ''", false)]         // 空表达式不匹配 Np
    public void IsLinePrintingCommand_VariousInputs_ReturnsExpected(string command, bool expected) {
        SedValidator.IsLinePrintingCommand(command).Should().Be(expected);
    }

    // ===== IsSubstitutionCommand 替换命令分类 =====

    [Fact]
    public void IsSubstitutionCommand_StandardSubstitution_ReturnsPassthrough() {
        var result = SedValidator.IsSubstitutionCommand("sed 's/a/b/'", false);
        result.Behavior.Should().Be(PermissionBehavior.Passthrough);
    }

    [Fact]
    public void IsSubstitutionCommand_WithGlobalFlag_ReturnsPassthrough() {
        var result = SedValidator.IsSubstitutionCommand("sed 's/a/b/g'", false);
        result.Behavior.Should().Be(PermissionBehavior.Passthrough);
    }

    [Fact]
    public void IsSubstitutionCommand_EmptyReplacement_ReturnsPassthrough() {
        // s/a/ 有 2 个 /,空替换,flags=""
        var result = SedValidator.IsSubstitutionCommand("sed 's/a/'", false);
        result.Behavior.Should().Be(PermissionBehavior.Passthrough);
    }

    [Fact]
    public void IsSubstitutionCommand_InPlaceWithoutAllow_ReturnsAsk() {
        var result = SedValidator.IsSubstitutionCommand("sed -i 's/a/b/'", false);
        result.Behavior.Should().Be(PermissionBehavior.Ask);
    }

    [Fact]
    public void IsSubstitutionCommand_InPlaceWithAllow_ReturnsPassthrough() {
        var result = SedValidator.IsSubstitutionCommand("sed -i 's/a/b/'", true);
        result.Behavior.Should().Be(PermissionBehavior.Passthrough);
    }

    [Theory]
    [InlineData("sed 'x/a/b/'", false)]      // 不以 s 开头
    [InlineData("sed 's|a|b|'", false)]      // 分隔符不是 /
    [InlineData("sed 's/a/b'", false)]       // 2 个 /,但 flags="b" 不匹配
    [InlineData("sed -x 's/a/b/'", false)]   // 不认识标志
    [InlineData("sed 's/a/b/x'", false)]     // 无效 flags
    public void IsSubstitutionCommand_InvalidExpressions_ReturnsDeny(string command, bool _) {
        var result = SedValidator.IsSubstitutionCommand(command, false);
        result.Behavior.Should().Be(PermissionBehavior.Deny);
    }

    [Fact]
    public void IsSubstitutionCommand_TwoExpressions_ReturnsDeny() {
        var result = SedValidator.IsSubstitutionCommand("sed -e 's/a/b/' -e 's/c/d/'", false);
        result.Behavior.Should().Be(PermissionBehavior.Deny);
    }

    [Fact]
    public void IsSubstitutionCommand_ExtendedRegexFlag_ReturnsPassthrough() {
        var result = SedValidator.IsSubstitutionCommand("sed -E 's/a/b/'", false);
        result.Behavior.Should().Be(PermissionBehavior.Passthrough);
    }

    // ===== ContainsDangerousOperations 危险操作黑名单 10 分支 =====

    [Fact]
    public void ContainsDangerousOperations_SafeExpression_ReturnsFalse() {
        SedValidator.ContainsDangerousOperations("s/a/b/").Should().BeFalse();
    }

    [Fact]
    public void ContainsDangerousOperations_NonAsciiChar_ReturnsTrue() {
        SedValidator.ContainsDangerousOperations("s/中/b/").Should().BeTrue();
    }

    [Theory]
    [InlineData("s/a/b/{cmd}", true)]   // 左花括号
    [InlineData("s/a/b/}end", true)]    // 右花括号
    [InlineData("s/a/b/#comment", true)] // 注释
    [InlineData("s/a/b/!cmd", true)]    // 否定操作符
    [InlineData("s/a/b/~step", true)]   // GNU 步进地址
    [InlineData(",addr", true)]         // 开头逗号
    [InlineData("w file", true)]        // 写文件命令 w
    [InlineData("W file", true)]        // 写文件命令 W
    [InlineData("e cmd", true)]         // 执行命令 e
    [InlineData("E cmd", true)]         // 执行命令 E
    public void ContainsDangerousOperations_DangerousPatterns_ReturnsTrue(string expr, bool _) {
        SedValidator.ContainsDangerousOperations(expr).Should().BeTrue();
    }

    [Fact]
    public void ContainsDangerousOperations_NewlineChar_ReturnsTrue() {
        SedValidator.ContainsDangerousOperations("s/a/b/\n").Should().BeTrue();
    }

    [Fact]
    public void ContainsDangerousOperations_TildeWithoutSpace_ReturnsTrue() {
        // ~ 任何位置都危险
        SedValidator.ContainsDangerousOperations("s/a/b/~").Should().BeTrue();
    }

    // ===== ExtractSedExpressions 表达式提取 =====

    [Fact]
    public void ExtractSedExpressions_ImplicitExpression_ReturnsSingle() {
        var exprs = SedValidator.ExtractSedExpressions("sed 's/a/b/'");
        exprs.Should().Equal(["s/a/b/"]);
    }

    [Fact]
    public void ExtractSedExpressions_ExplicitExpressionFlag_ReturnsSingle() {
        var exprs = SedValidator.ExtractSedExpressions("sed -e 's/a/b/'");
        exprs.Should().Equal(["s/a/b/"]);
    }

    [Fact]
    public void ExtractSedExpressions_MultipleExplicitExpressions_ReturnsAll() {
        var exprs = SedValidator.ExtractSedExpressions("sed -e 's/a/b/' -e 's/c/d/'");
        exprs.Should().Equal(["s/a/b/", "s/c/d/"]);
    }

    [Fact]
    public void ExtractSedExpressions_ExpressionEqualsForm_ReturnsSingle() {
        var exprs = SedValidator.ExtractSedExpressions("sed --expression='s/a/b/'");
        exprs.Should().Equal(["s/a/b/"]);
    }

    [Fact]
    public void ExtractSedExpressions_WithFileArgument_ReturnsOnlyExpression() {
        // 文件参数不算表达式
        var exprs = SedValidator.ExtractSedExpressions("sed 's/a/b/' file.txt");
        exprs.Should().Equal(["s/a/b/"]);
    }

    [Fact]
    public void ExtractSedExpressions_NoExpression_ReturnsEmpty() {
        var exprs = SedValidator.ExtractSedExpressions("sed -n");
        exprs.Should().BeEmpty();
    }

    // ===== CountUnescapedSlashes 未转义 / 计数 =====

    [Theory]
    [InlineData("s/a/b/", 3)]        // 标准 3 个 /
    [InlineData("s/a/b", 2)]         // 2 个 /(尾随 / 省略)
    [InlineData("s/a/", 2)]          // 空替换 2 个 /
    [InlineData("s//", 2)]           // 2 个连续 /
    [InlineData("s/a\\/b/", 2)]      // 索引 4 的 / 被转义,计数 2(索引 1 和 6)
    [InlineData("s", 0)]             // 无 /
    [InlineData("", 0)]              // 空字符串
    [InlineData("/", 0)]             // 索引 0 的 / 不计数(循环从 1 开始)
    [InlineData("s/a/b/g", 3)]       // 3 个 /
    public void CountUnescapedSlashes_VariousInputs_ReturnsExpected(string expr, int expected) {
        SedValidator.CountUnescapedSlashes(expr).Should().Be(expected);
    }
}
