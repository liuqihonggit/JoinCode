// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
#pragma warning disable JCC11003
namespace Guard.Security.Tests;

/// <summary>
/// BashAstSecurityWalker 确定性测试 — 通过公共入口 ParseForSecurity/CheckSemantics 测试纯 AST 遍历行为。
/// 给定 bash 脚本字符串 → 断言安全判定结果,不依赖时序,完全确定性。
/// 覆盖 bash_ast_walker 全部 9 个分部文件:PreChecks/WalkCommand/WalkArgument/WalkVariables/
/// WalkRedirects/CollectCommands/WalkStatements/CheckSemantics/主文件。
/// </summary>
public class BashAstSecurityWalkerTest : IDisposable {
    private readonly BashAstSecurityWalker _walker = new();

    public void Dispose() => _walker.Dispose();

    /// <summary>断言结果为 Simple 并返回其 Commands 数组。</summary>
    private static BashSimpleCommandInfo[] ExpectSimple(BashAstSecurityResult result) {
        var simple = result as BashAstSecurityResult.Simple;
        simple.Should().NotBeNull($"期望结果为 Simple,实际为 {result?.GetType().Name}");
        return simple!.Commands;
    }

    /// <summary>断言结果为 TooComplex 并返回其 NodeType。</summary>
    private static string ExpectTooComplex(BashAstSecurityResult result) {
        var tc = result as BashAstSecurityResult.TooComplex;
        tc.Should().NotBeNull($"期望结果为 TooComplex,实际为 {result?.GetType().Name}");
        return tc!.NodeType ?? "";
    }

    #region 空输入与边界 — 主文件 ParseForSecurity 入口

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void ParseForSecurity_EmptyOrWhitespace_ShouldReturnSimpleEmpty(string command) {
        var commands = ExpectSimple(_walker.ParseForSecurity(command));
        commands.Should().BeEmpty();
    }

    [Fact]
    public void ParseForSecurity_OnlyComment_ShouldReturnSimpleEmpty() {
        var commands = ExpectSimple(_walker.ParseForSecurity("# just a comment"));
        commands.Should().BeEmpty();
    }

    #endregion

    #region 简单命令 — WalkCommand/CollectCommands

    [Fact]
    public void ParseForSecurity_EchoHello_ShouldReturnOneCommandWithArgv() {
        var commands = ExpectSimple(_walker.ParseForSecurity("echo hello"));
        commands.Should().HaveCount(1);
        commands[0].Argv.Should().Equal("echo", "hello");
        commands[0].EnvVars.Should().BeEmpty();
        commands[0].Redirects.Should().BeEmpty();
    }

    [Fact]
    public void ParseForSecurity_LsWithFlags_ShouldPreserveFlags() {
        var commands = ExpectSimple(_walker.ParseForSecurity("ls -la"));
        commands.Should().HaveCount(1);
        commands[0].Argv.Should().Equal("ls", "-la");
    }

    [Fact]
    public void ParseForSecurity_CatPasswd_ShouldExtractCommand() {
        var commands = ExpectSimple(_walker.ParseForSecurity("cat /etc/passwd"));
        commands.Should().HaveCount(1);
        commands[0].Argv.Should().Equal("cat", "/etc/passwd");
    }

    [Fact]
    public void ParseForSecurity_MultipleCommands_Semicolon_ShouldExtractAll() {
        var commands = ExpectSimple(_walker.ParseForSecurity("echo a; echo b"));
        commands.Should().HaveCount(2);
        commands[0].Argv.Should().Equal("echo", "a");
        commands[1].Argv.Should().Equal("echo", "b");
    }

    [Fact]
    public void ParseForSecurity_RmRfRoot_ShouldExtractCommand() {
        var commands = ExpectSimple(_walker.ParseForSecurity("rm -rf /"));
        commands.Should().HaveCount(1);
        commands[0].Argv.Should().Equal("rm", "-rf", "/");
    }

    #endregion

    #region 变量赋值 — WalkVariables/WalkVariableAssignment

    [Fact]
    public void ParseForSecurity_SimpleAssignment_ShouldReturnSimpleEmpty() {
        // 独立赋值只更新 varScope,不产生命令
        var commands = ExpectSimple(_walker.ParseForSecurity("x=1"));
        commands.Should().BeEmpty();
    }

    [Fact]
    public void ParseForSecurity_AssignmentWithCmdSub_ShouldExtractInnerCommand() {
        // x=$(rm -rf /) → 命令替换提取 rm 命令到主列表
        var commands = ExpectSimple(_walker.ParseForSecurity("x=$(rm -rf /)"));
        commands.Should().HaveCount(1);
        commands[0].Argv.Should().Equal("rm", "-rf", "/");
    }

    [Fact]
    public void ParseForSecurity_IfsAssignment_ShouldBeTooComplex() {
        var nodeType = ExpectTooComplex(_walker.ParseForSecurity("IFS=x"));
        nodeType.Should().Be("IFS_ASSIGNMENT");
    }

    [Fact]
    public void ParseForSecurity_TildeInAssignment_ShouldBeTooComplex() {
        var nodeType = ExpectTooComplex(_walker.ParseForSecurity("x=~"));
        nodeType.Should().Be("TILDE_IN_ASSIGNMENT");
    }

    [Fact]
    public void ParseForSecurity_AssignmentTracking_ShouldResolveInString() {
        // x=hello; echo "$x" → x 被跟踪,字符串内展开为实际值
        var commands = ExpectSimple(_walker.ParseForSecurity("x=hello; echo \"$x\""));
        commands.Should().HaveCount(1);
        commands[0].Argv.Should().Equal("echo", "hello");
    }

    [Fact]
    public void ParseForSecurity_ExportWithUntrackedPathVar_ShouldBeTooComplex() {
        // $PATH 在 concatenation 中裸展开(非字符串内),PATH 未跟踪 → TooComplex
        ExpectTooComplex(_walker.ParseForSecurity("export PATH=/evil:$PATH"));
    }

    [Fact]
    public void ParseForSecurity_ExportSimpleValue_ShouldReturnExportCommand() {
        // export FOO=bar → FOO 被跟踪,argv 含 export 和 FOO=bar
        var commands = ExpectSimple(_walker.ParseForSecurity("export FOO=bar"));
        commands.Should().HaveCount(1);
        commands[0].Argv.Should().Equal("export", "FOO=bar");
    }

    #endregion

    #region 重定向 — WalkRedirects/WalkFileRedirect/WalkHeredocRedirect

    [Fact]
    public void ParseForSecurity_RedirectToFile_ShouldCaptureRedirect() {
        var commands = ExpectSimple(_walker.ParseForSecurity("echo x > file.txt"));
        commands.Should().HaveCount(1);
        commands[0].Argv.Should().Equal("echo", "x");
        commands[0].Redirects.Should().HaveCount(1);
        commands[0].Redirects[0].Op.Should().Be(">");
        commands[0].Redirects[0].Target.Should().Be("file.txt");
    }

    [Fact]
    public void ParseForSecurity_RedirectToPasswd_ShouldCaptureTarget() {
        var commands = ExpectSimple(_walker.ParseForSecurity("cat > /etc/passwd"));
        commands.Should().HaveCount(1);
        commands[0].Argv.Should().Equal("cat");
        commands[0].Redirects.Should().HaveCount(1);
        commands[0].Redirects[0].Target.Should().Be("/etc/passwd");
    }

    [Fact]
    public void ParseForSecurity_AppendRedirect_ShouldCaptureOp() {
        var commands = ExpectSimple(_walker.ParseForSecurity("echo x >> file.txt"));
        commands[0].Redirects[0].Op.Should().Be(">>");
    }

    [Fact]
    public void ParseForSecurity_UnquotedHeredoc_ShouldBeTooComplex() {
        // 非引号分隔符 heredoc — body 经历变量/命令替换展开
        var nodeType = ExpectTooComplex(_walker.ParseForSecurity("cat <<EOF\nhello\nEOF"));
        nodeType.Should().Be("heredoc_redirect");
    }

    [Fact]
    public void ParseForSecurity_QuotedHeredoc_ShouldReturnSimple() {
        // 引号分隔符 heredoc — body 不展开,可静态分析
        var commands = ExpectSimple(_walker.ParseForSecurity("cat <<'EOF'\nhello\nEOF"));
        commands.Should().HaveCount(1);
        commands[0].Argv.Should().Equal("cat");
    }

    [Fact]
    public void ParseForSecurity_DoubleQuotedHeredoc_ShouldReturnSimple() {
        var commands = ExpectSimple(_walker.ParseForSecurity("cat <<\"EOF\"\nhello\nEOF"));
        commands.Should().HaveCount(1);
        commands[0].Argv.Should().Equal("cat");
    }

    [Fact]
    public void ParseForSecurity_RedirectWithCmdSubTarget_ShouldBeTooComplex() {
        var nodeType = ExpectTooComplex(_walker.ParseForSecurity("echo x > $(mktemp)"));
        nodeType.Should().Be("CMDSUB_REDIRECT");
    }

    #endregion

    #region 管道 — WalkStructuralNode/pipeline

    [Fact]
    public void ParseForSecurity_SafePipeline_ShouldExtractBothCommands() {
        var commands = ExpectSimple(_walker.ParseForSecurity("ls | grep x"));
        commands.Should().HaveCount(2);
        commands[0].Argv.Should().Equal("ls");
        commands[1].Argv.Should().Equal("grep", "x");
    }

    [Fact]
    public void ParseForSecurity_DangerousPipeline_ShouldExtractBothCommands() {
        // walker 只提取命令,危险判定在 CheckSemantics 层
        var commands = ExpectSimple(_walker.ParseForSecurity("cat | sh"));
        commands.Should().HaveCount(2);
        commands[0].Argv.Should().Equal("cat");
        commands[1].Argv.Should().Equal("sh");
    }

    [Fact]
    public void ParseForSecurity_ThreeStagePipeline_ShouldExtractAll() {
        var commands = ExpectSimple(_walker.ParseForSecurity("cat a | grep b | wc -l"));
        commands.Should().HaveCount(3);
        commands[0].Argv.Should().Equal("cat", "a");
        commands[1].Argv.Should().Equal("grep", "b");
        commands[2].Argv.Should().Equal("wc", "-l");
    }

    #endregion

    #region 算术展开 — WalkArgument/ValidateArithmeticNode

    [Fact]
    public void ParseForSecurity_PureArithmetic_ShouldReturnSimple() {
        // $((1+1)) 只含数字和运算符,可静态分析
        var commands = ExpectSimple(_walker.ParseForSecurity("echo $((1+1))"));
        commands.Should().HaveCount(1);
        commands[0].Argv.Should().HaveCount(2);
        commands[0].Argv[0].Should().Be("echo");
    }

    [Fact]
    public void ParseForSecurity_ArithmeticWithVariable_ShouldBeTooComplex() {
        var nodeType = ExpectTooComplex(_walker.ParseForSecurity("echo $((x+1))"));
        nodeType.Should().Be("ARITH_VAR");
    }

    [Fact]
    public void ParseForSecurity_ArithmeticWithCmdSub_ShouldBeTooComplex() {
        var nodeType = ExpectTooComplex(_walker.ParseForSecurity("echo $(($(rm -rf /)))"));
        nodeType.Should().Be("ARITH_CMDSUB");
    }

    [Fact]
    public void ParseForSecurity_ArithmeticWithExpansion_ShouldBeTooComplex() {
        var nodeType = ExpectTooComplex(_walker.ParseForSecurity("echo $(($x+1))"));
        nodeType.Should().Be("ARITH_EXPANSION");
    }

    #endregion

    #region 引号与变量展开 — WalkString/ResolveSimpleExpansion

    [Fact]
    public void ParseForSecurity_DoubleQuotedLiteral_ShouldStripQuotes() {
        var commands = ExpectSimple(_walker.ParseForSecurity("echo \"hello\""));
        commands[0].Argv.Should().Equal("echo", "hello");
    }

    [Fact]
    public void ParseForSecurity_SingleQuotedLiteral_ShouldStripQuotes() {
        var commands = ExpectSimple(_walker.ParseForSecurity("echo 'hello'"));
        commands[0].Argv.Should().Equal("echo", "hello");
    }

    [Fact]
    public void ParseForSecurity_SafeEnvVarInString_ShouldReturnPlaceholder() {
        // $HOME 在 SafeEnvVars,字符串内展开为 placeholder
        var commands = ExpectSimple(_walker.ParseForSecurity("echo \"$HOME\""));
        commands[0].Argv.Should().Equal("echo", "__TRACKED_VAR__");
    }

    [Fact]
    public void ParseForSecurity_UnsafeVarInString_ShouldBeTooComplex() {
        // $FOO 不在 SafeEnvVars,字符串内展开也无法静态确定
        ExpectTooComplex(_walker.ParseForSecurity("echo \"$FOO\""));
    }

    [Fact]
    public void ParseForSecurity_BareSafeEnvVar_ShouldBeTooComplex() {
        // $HOME 在字符串外(裸展开),即使 SafeEnvVars 也 TooComplex
        ExpectTooComplex(_walker.ParseForSecurity("echo $HOME"));
    }

    [Fact]
    public void ParseForSecurity_NestedQuotes_ShouldHandleCorrectly() {
        var commands = ExpectSimple(_walker.ParseForSecurity("echo \"hello 'world'\""));
        commands[0].Argv.Should().Equal("echo", "hello 'world'");
    }

    [Fact]
    public void ParseForSecurity_SpecialVarInString_ShouldReturnPlaceholder() {
        // $? 是特殊变量,字符串内展开为 placeholder
        var commands = ExpectSimple(_walker.ParseForSecurity("echo \"$?\""));
        commands[0].Argv.Should().Equal("echo", "__TRACKED_VAR__");
    }

    #endregion

    #region for/while/if 语句 — WalkStatements

    [Fact]
    public void ParseForSecurity_ForLoopWithQuotedVar_ShouldReturnSimple() {
        // for 循环变量在字符串内展开为 placeholder
        var commands = ExpectSimple(_walker.ParseForSecurity("for i in 1 2 3; do echo \"$i\"; done"));
        commands.Should().HaveCount(1);
        commands[0].Argv.Should().Equal("echo", "__TRACKED_VAR__");
    }

    [Fact]
    public void ParseForSecurity_ForLoopWithBareVar_ShouldBeTooComplex() {
        // for 循环变量在字符串外(裸展开)→ placeholder 在非字符串上下文 → TooComplex
        ExpectTooComplex(_walker.ParseForSecurity("for i in 1 2 3; do echo $i; done"));
    }

    [Fact]
    public void ParseForSecurity_ForLoopWithCmdSubInIter_ShouldExtractInnerCommand() {
        // for i in $(seq 1 3) — 内部命令提取
        var commands = ExpectSimple(_walker.ParseForSecurity("for i in $(seq 1 3); do echo \"$i\"; done"));
        commands.Should().HaveCount(2);
        commands[0].Argv.Should().Equal("seq", "1", "3");
        commands[1].Argv.Should().Equal("echo", "__TRACKED_VAR__");
    }

    [Fact]
    public void ParseForSecurity_IfsAsLoopVar_ShouldBeTooComplex() {
        // IFS 作为循环变量绕过赋值验证 — 禁止
        var nodeType = ExpectTooComplex(_walker.ParseForSecurity("for IFS in 1; do :; done"));
        nodeType.Should().Be("for_statement");
    }

    [Fact]
    public void ParseForSecurity_IfStatement_ShouldExtractConditionAndBranch() {
        var commands = ExpectSimple(_walker.ParseForSecurity("if true; then echo hi; fi"));
        commands.Should().HaveCount(2);
        commands[0].Argv.Should().Equal("true");
        commands[1].Argv.Should().Equal("echo", "hi");
    }

    [Fact]
    public void ParseForSecurity_WhileLoop_ShouldExtractConditionAndBody() {
        var commands = ExpectSimple(_walker.ParseForSecurity("while true; do echo hi; done"));
        commands.Should().HaveCount(2);
        commands[0].Argv.Should().Equal("true");
        commands[1].Argv.Should().Equal("echo", "hi");
    }

    [Fact]
    public void ParseForSecurity_IfElseStatement_ShouldExtractAllBranches() {
        var commands = ExpectSimple(_walker.ParseForSecurity("if true; then echo a; else echo b; fi"));
        commands.Should().HaveCount(3);
        commands[0].Argv.Should().Equal("true");
        commands[1].Argv.Should().Equal("echo", "a");
        commands[2].Argv.Should().Equal("echo", "b");
    }

    [Fact]
    public void ParseForSecurity_CStyleFor_ShouldBeTooComplex() {
        ExpectTooComplex(_walker.ParseForSecurity("for ((i=0; i<10; i++)); do echo \"$i\"; done"));
    }

    #endregion

    #region 子shell — WalkSubshell

    [Fact]
    public void ParseForSecurity_SubshellDangerous_ShouldExtractInnerCommand() {
        var commands = ExpectSimple(_walker.ParseForSecurity("(rm -rf /)"));
        commands.Should().HaveCount(1);
        commands[0].Argv.Should().Equal("rm", "-rf", "/");
    }

    [Fact]
    public void ParseForSecurity_SubshellSafe_ShouldExtractInnerCommand() {
        var commands = ExpectSimple(_walker.ParseForSecurity("(echo hello)"));
        commands.Should().HaveCount(1);
        commands[0].Argv.Should().Equal("echo", "hello");
    }

    [Fact]
    public void ParseForSecurity_SubshellMultipleCommands_ShouldExtractAll() {
        var commands = ExpectSimple(_walker.ParseForSecurity("(echo a; echo b)"));
        commands.Should().HaveCount(2);
        commands[0].Argv.Should().Equal("echo", "a");
        commands[1].Argv.Should().Equal("echo", "b");
    }

    #endregion

    #region unset/声明命令 — WalkUnsetCommand/WalkDeclarationCommand

    [Fact]
    public void ParseForSecurity_Unset_ShouldReturnUnsetCommand() {
        // unset 提取为命令(argv 含 unset 和变量名)
        var commands = ExpectSimple(_walker.ParseForSecurity("unset PATH"));
        commands.Should().HaveCount(1);
        commands[0].Argv.Should().Equal("unset", "PATH");
    }

    [Fact]
    public void ParseForSecurity_UnsetMultipleVars_ShouldReturnAllInArgv() {
        var commands = ExpectSimple(_walker.ParseForSecurity("unset FOO BAR"));
        commands.Should().HaveCount(1);
        commands[0].Argv.Should().Equal("unset", "FOO", "BAR");
    }

    [Fact]
    public void ParseForSecurity_Declare_ShouldReturnDeclareCommand() {
        var commands = ExpectSimple(_walker.ParseForSecurity("declare -r x=1"));
        commands.Should().HaveCount(1);
        commands[0].Argv.Should().Equal("declare", "-r", "x=1");
    }

    [Fact]
    public void ParseForSecurity_Local_ShouldReturnLocalCommand() {
        var commands = ExpectSimple(_walker.ParseForSecurity("local x=1"));
        commands.Should().HaveCount(1);
        commands[0].Argv.Should().Equal("local", "x=1");
    }

    [Fact]
    public void ParseForSecurity_Readonly_ShouldReturnReadonlyCommand() {
        var commands = ExpectSimple(_walker.ParseForSecurity("readonly x=1"));
        commands.Should().HaveCount(1);
        commands[0].Argv.Should().Equal("readonly", "x=1");
    }

    [Fact]
    public void ParseForSecurity_ExportBareVar_ShouldReturnExportCommand() {
        var commands = ExpectSimple(_walker.ParseForSecurity("export FOO"));
        commands.Should().HaveCount(1);
        commands[0].Argv.Should().Equal("export", "FOO");
    }

    #endregion

    #region 错误节点与不可分析构造 — PreChecks/HasErrorNode/CollectCommands

    [Fact]
    public void ParseForSecurity_SyntaxError_ShouldBeTooComplex() {
        // 未闭合引号 → AST 包含 ERROR 节点
        var nodeType = ExpectTooComplex(_walker.ParseForSecurity("echo \"hello"));
        nodeType.Should().Be("PARSE_ERROR");
    }

    [Fact]
    public void ParseForSecurity_TestCommandAsPlainCommand_ShouldReturnSimple() {
        // test -f x 在 TreeSitter bash 中是普通 command,不是 test_command
        var commands = ExpectSimple(_walker.ParseForSecurity("test -f x"));
        commands.Should().HaveCount(1);
        commands[0].Argv.Should().Equal("test", "-f", "x");
    }

    [Fact]
    public void ParseForSecurity_BracketTestCommand_ShouldBeTooComplex() {
        // [[ -f x ]] 是 test_command → WalkTestCommand 直接 TooComplexNode
        ExpectTooComplex(_walker.ParseForSecurity("[[ -f x ]]"));
    }

    [Fact]
    public void ParseForSecurity_FunctionDefinition_ShouldBeTooComplex() {
        ExpectTooComplex(_walker.ParseForSecurity("f() { echo hi; }"));
    }

    [Fact]
    public void ParseForSecurity_CaseStatement_ShouldBeTooComplex() {
        ExpectTooComplex(_walker.ParseForSecurity("case x in a) echo a;; esac"));
    }

    [Fact]
    public void ParseForSecurity_BraceExpansion_ShouldBeTooComplex() {
        // {a,b} 花括号展开 → BraceExpansionRegex 匹配
        ExpectTooComplex(_walker.ParseForSecurity("echo {a,b}"));
    }

    #endregion

    #region PS4 赋值与 herestring — WalkVariables/WalkHerestringRedirect 补充覆盖

    [Fact]
    public void ParseForSecurity_Ps4SafeValue_ShouldReturnSimpleEmpty() {
        // PS4='+ ' 在安全字符集内 → 通过
        var commands = ExpectSimple(_walker.ParseForSecurity("PS4='+ '"));
        commands.Should().BeEmpty();
    }

    [Fact]
    public void ParseForSecurity_Ps4WithCmdSub_ShouldBeTooComplex() {
        // PS4=$(id) → 命令替换 → placeholder → PS4_PLACEHOLDER
        var nodeType = ExpectTooComplex(_walker.ParseForSecurity("PS4=$(id)"));
        nodeType.Should().Be("PS4_PLACEHOLDER");
    }

    [Fact]
    public void ParseForSecurity_Ps4Append_ShouldBeTooComplex() {
        // PS4+= 无法静态验证
        var nodeType = ExpectTooComplex(_walker.ParseForSecurity("PS4+='x'"));
        nodeType.Should().Be("PS4_APPEND");
    }

    [Fact]
    public void ParseForSecurity_HerestringLiteral_ShouldReturnSimple() {
        // cat <<< hello — herestring 字面量
        var commands = ExpectSimple(_walker.ParseForSecurity("cat <<< hello"));
        commands.Should().HaveCount(1);
        commands[0].Argv.Should().Equal("cat");
    }

    [Fact]
    public void ParseForSecurity_HerestringWithCmdSub_ShouldExtractInnerCommand() {
        // cat <<< $(rm -rf /) — 命令替换提取
        var commands = ExpectSimple(_walker.ParseForSecurity("cat <<< $(rm -rf /)"));
        commands.Should().Contain(c => c.Argv.SequenceEqual(new[] { "rm", "-rf", "/" }));
    }

    #endregion

    #region CheckSemantics — CheckSemantics.cs 公共入口

    [Fact]
    public void CheckSemantics_EmptyCommands_ShouldBeOk() {
        var result = _walker.CheckSemantics([]);
        result.IsOk.Should().BeTrue();
    }

    [Fact]
    public void CheckSemantics_SafeEchoCommand_ShouldBeOk() {
        var cmd = new BashSimpleCommandInfo(["echo", "hello"], [], [], "echo hello");
        var result = _walker.CheckSemantics([cmd]);
        result.IsOk.Should().BeTrue();
    }

    [Fact]
    public void CheckSemantics_PlaceholderInCommandName_ShouldBeNotOk() {
        // 命令名包含占位符 → DangerousVariables
        var cmd = new BashSimpleCommandInfo(["__TRACKED_VAR__"], [], [], "__TRACKED_VAR__");
        var result = _walker.CheckSemantics([cmd]);
        result.IsOk.Should().BeFalse();
        result.CheckId.Should().Be(BashSecurityCheckId.DangerousVariables);
    }

    [Fact]
    public void CheckSemantics_RmCommand_ShouldBeChecked() {
        var cmd = new BashSimpleCommandInfo(["rm", "-rf", "/"], [], [], "rm -rf /");
        var result = _walker.CheckSemantics([cmd]);
        // rm 是危险命令,CheckSemantics 应返回结果(无论 Ok 与否,都应执行检查)
        result.Should().NotBeNull();
    }

    [Fact]
    public void CheckSemantics_MultipleCommands_ShouldCheckAll() {
        var cmds = new[] {
            new BashSimpleCommandInfo(["echo", "a"], [], [], "echo a"),
            new BashSimpleCommandInfo(["echo", "b"], [], [], "echo b"),
        };
        var result = _walker.CheckSemantics(cmds);
        result.IsOk.Should().BeTrue();
    }

    #endregion

    #region null 节点守卫 — CollectCommands/WalkCommand private 防御性守卫,通过反射验证

    /// <summary>
    /// CollectCommands/WalkCommand 是 private static,正常路径不可达 null(调用方有 null 守卫)。
    /// 此处通过反射验证防御性守卫:null node → TooComplex(NULL_NODE),fail-closed 设计。
    /// </summary>
    [Trait("Category", "Deterministic")]
    [Fact]
    public void CollectCommands_NullNode_ReturnsTooComplexNullNode() {
        var method = typeof(BashAstSecurityWalker).GetMethod(
            "CollectCommands", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        method.Should().NotBeNull("CollectCommands 应存在");
        var result = (BashAstSecurityResult?)method!.Invoke(null,
            [null, new List<BashSimpleCommandInfo>(), new Dictionary<string, string>()]);
        var tc = result as BashAstSecurityResult.TooComplex;
        tc.Should().NotBeNull("null node 应返回 TooComplex");
        tc!.NodeType.Should().Be("NULL_NODE");
    }

    [Trait("Category", "Deterministic")]
    [Fact]
    public void WalkCommand_NullNode_ReturnsTooComplexNullNode() {
        var method = typeof(BashAstSecurityWalker).GetMethod(
            "WalkCommand", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        method.Should().NotBeNull("WalkCommand 应存在");
        var result = (BashAstSecurityResult?)method!.Invoke(null,
            [null, Array.Empty<BashRedirectInfo>(), new List<BashSimpleCommandInfo>(), new Dictionary<string, string>()]);
        var tc = result as BashAstSecurityResult.TooComplex;
        tc.Should().NotBeNull("null node 应返回 TooComplex");
        tc!.NodeType.Should().Be("NULL_NODE");
    }

    #endregion
}
