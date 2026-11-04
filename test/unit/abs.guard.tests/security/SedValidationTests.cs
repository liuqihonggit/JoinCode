namespace Abs.Tests.Security;

/// <summary>
/// SedValidation 确定性单元测试 — 覆盖 ExtractSedExpressions(internal) + ContainsDangerousOperations(public)。
/// 纯字符串解析,不依赖时序/IO。
/// </summary>
public sealed class SedValidationTests {

    // ── ExtractSedExpressions: 表达式提取 ──

    [Fact]
    public void Extract_SimpleSubstitution_ReturnsOneExpression() {
        var r = SedValidation.ExtractSedExpressions("sed 's/old/new/g'");
        r.Success.Should().BeTrue();
        r.Expressions.Should().ContainSingle().Which.Should().Be("s/old/new/g");
    }

    [Fact]
    public void Extract_TwoExpressionsWithEFlag_ReturnsBoth() {
        var r = SedValidation.ExtractSedExpressions("sed -e 's/a/b/' -e 's/c/d/'");
        r.Success.Should().BeTrue();
        r.Expressions.Should().HaveCount(2);
        r.Expressions[0].Should().Be("s/a/b/");
        r.Expressions[1].Should().Be("s/c/d/");
    }

    [Fact]
    public void Extract_AddressRangeExpression_ReturnsExpression() {
        var r = SedValidation.ExtractSedExpressions("sed '1,5d'");
        r.Success.Should().BeTrue();
        r.Expressions.Should().ContainSingle().Which.Should().Be("1,5d");
    }

    [Fact]
    public void Extract_NonSedCommand_ReturnsFailure() {
        var r = SedValidation.ExtractSedExpressions("awk '{print}'");
        r.Success.Should().BeFalse();
    }

    [Fact]
    public void Extract_EmptyCommand_ReturnsFailure() {
        SedValidation.ExtractSedExpressions("").Success.Should().BeFalse();
        SedValidation.ExtractSedExpressions("   ").Success.Should().BeFalse();
    }

    [Fact]
    public void Extract_InPlaceFlag_SetsHasInPlace() {
        var r = SedValidation.ExtractSedExpressions("sed -i 's/a/b/' file.txt");
        r.Success.Should().BeTrue();
        r.HasInPlace.Should().BeTrue();
    }

    [Fact]
    public void Extract_FileArg_Populated() {
        var r = SedValidation.ExtractSedExpressions("sed 's/a/b/' file.txt");
        r.Success.Should().BeTrue();
        r.FileArgs.Should().Contain("file.txt");
    }

    [Fact]
    public void Extract_EFlagFused_ShortForm() {
        // -e 融合形式: -e's/old/new/g'
        var r = SedValidation.ExtractSedExpressions("sed -ne's/a/b/g'");
        r.Success.Should().BeTrue();
        r.Expressions.Should().ContainSingle();
    }

    [Fact]
    public void Extract_ExpressionEqualsForm() {
        var r = SedValidation.ExtractSedExpressions("sed --expression=s/a/b/g");
        r.Success.Should().BeTrue();
        r.Expressions.Should().ContainSingle().Which.Should().Be("s/a/b/g");
    }

    [Fact]
    public void Extract_DoubleDashTerminatesFlags() {
        var r = SedValidation.ExtractSedExpressions("sed 's/a/b/' -- file.txt");
        r.Success.Should().BeTrue();
        r.FileArgs.Should().Contain("file.txt");
    }

    // ── ContainsDangerousOperations: 危险操作检测 ──

    [Fact]
    public void Dangerous_EmptyOrWhitespace_ReturnsTrue() {
        SedValidation.ContainsDangerousOperations("").Should().BeTrue();
        SedValidation.ContainsDangerousOperations("   ").Should().BeTrue();
    }

    [Fact]
    public void Dangerous_SemicolonInExpression_NotTopLevelChecked() {
        // 注:ContainsDangerousOperations 顶层不检查分号;分号由 IsSubstitutionCommand 允许列表拦截。
        // 纯分号表达式(无 w/e)不被拒绝列表视为危险。
        SedValidation.ContainsDangerousOperations("sed 's/a/b/;p'").Should().BeFalse();
    }

    [Fact]
    public void Dangerous_SemicolonWithWrite_IsSubstitutionBlocked() {
        // 含 w 命令的分号表达式 → IsSedCommandSafe 应返回 false(允许列表拒绝分号)
        SedValidation.IsSedCommandSafe("sed 's/a/b/;w file'").Should().BeFalse();
    }

    [Fact]
    public void Dangerous_WriteCommandInExpression_ReturnsTrue() {
        // 1w file 形式 → ContainsWriteCommand 检测到 w 后跟空格
        SedValidation.ContainsDangerousOperations("sed '1w file'").Should().BeTrue();
    }

    [Fact]
    public void Dangerous_ExecuteCommandInExpression_ReturnsTrue() {
        // 1e cmd 形式 → ContainsExecuteCommand 检测到 e 后跟空格
        SedValidation.ContainsDangerousOperations("sed '1e cmd'").Should().BeTrue();
    }

    [Fact]
    public void Dangerous_BraceBlock_ReturnsTrue() {
        SedValidation.ContainsDangerousOperations("sed '{s/a/b/}'").Should().BeTrue();
    }

    [Fact]
    public void Dangerous_NewlineInsideExpression_ReturnsTrue() {
        // 表达式内部的换行(不被 command.Trim() 去除)
        SedValidation.ContainsDangerousOperations("sed 's/a/b/\np'").Should().BeTrue();
    }

    [Fact]
    public void Dangerous_NewlineAtTail_TrimmedAway_ReturnsFalse() {
        // 命令尾部换行被 Trim 去除 → 不视为危险
        SedValidation.ContainsDangerousOperations("sed 's/a/b/'\n").Should().BeFalse();
    }

    [Fact]
    public void Dangerous_NegationOperator_ReturnsTrue() {
        SedValidation.ContainsDangerousOperations("sed '1!d'").Should().BeTrue();
    }

    [Fact]
    public void Dangerous_GnuStepAddress_ReturnsTrue() {
        SedValidation.ContainsDangerousOperations("sed '0~2p'").Should().BeTrue();
    }

    [Fact]
    public void Dangerous_NonAscii_ReturnsTrue() {
        // Unicode 同形字攻击
        SedValidation.ContainsDangerousOperations("sed 's/а/b/'").Should().BeTrue();
    }

    [Fact]
    public void Dangerous_SafeSubstitution_ReturnsFalse() {
        SedValidation.ContainsDangerousOperations("sed 's/a/b/g'").Should().BeFalse();
    }

    [Fact]
    public void Dangerous_SafeSubstitutionWithFlags_ReturnsFalse() {
        SedValidation.ContainsDangerousOperations("sed 's/a/b/gi'").Should().BeFalse();
    }

    [Fact]
    public void Dangerous_SafeLinePrint_ReturnsFalse() {
        SedValidation.ContainsDangerousOperations("sed -n '1,5p'").Should().BeFalse();
    }

    [Fact]
    public void Dangerous_HashAsSeparatorAfterS_ReturnsFalse() {
        // s#old#new# 形式: # 紧跟 s 后作为分隔符,允许
        SedValidation.ContainsDangerousOperations("sed 's#a#b#'").Should().BeFalse();
    }

    [Fact]
    public void Dangerous_HashNotAfterS_ReturnsTrue() {
        // # 不跟在 s 后 → 注入风险
        SedValidation.ContainsDangerousOperations("sed '1d#x'").Should().BeTrue();
    }
}
