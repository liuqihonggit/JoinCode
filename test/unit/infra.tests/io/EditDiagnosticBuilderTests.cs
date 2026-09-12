namespace JoinCode.Infra.Tests.IO;

public sealed class EditDiagnosticBuilderTests
{
    [Fact]
    public void BuildDiagnostic_StringNotFound_ReturnsStringNotFoundReason()
    {
        var fileContent = "line1\nline2\nline3\n";
        var oldString = "completely_nonexistent_line";

        var diag = EditDiagnosticBuilder.BuildDiagnostic(fileContent, oldString);

        diag.Reason.Should().Be(EditMismatchReason.StringNotFound);
        diag.MatchedLine.Should().BeNull();
        diag.FormattedMessage.Should().Contain("StringNotFound");
        diag.FormattedMessage.Should().Contain("首行在文件中未找到任何匹配");
    }

    [Fact]
    public void BuildDiagnostic_PartialMatch_ReportsLineNumberAndDiverge()
    {
        var fileContent = "line1\nline2_correct\nline3\nline4\n";
        var oldString = "line1\nline2_WRONG\nline3\n";

        var diag = EditDiagnosticBuilder.BuildDiagnostic(fileContent, oldString);

        diag.Reason.Should().Be(EditMismatchReason.PartialMatch);
        diag.MatchedLine.Should().Be(1);
        diag.MatchedLineCount.Should().Be(1);
        diag.DivergeLine.Should().Be(2);
        diag.FileLineAtDiverge.Should().Be("line2_correct");
        diag.OldStringLineAtDiverge.Should().Be("line2_WRONG");
        diag.FormattedMessage.Should().Contain("PartialMatch");
        diag.FormattedMessage.Should().Contain("第 1 行找到");
        diag.FormattedMessage.Should().Contain("第 2 行开始分叉");
    }

    [Fact]
    public void BuildDiagnostic_PartialMatch_MiddleOfFile_ReportsCorrectLine()
    {
        var fileContent = "alpha\nbeta\ngamma\ndelta\nepsilon\n";
        var oldString = "gamma\ndelta_WRONG\nepsilon\n";

        var diag = EditDiagnosticBuilder.BuildDiagnostic(fileContent, oldString);

        diag.Reason.Should().Be(EditMismatchReason.PartialMatch);
        diag.MatchedLine.Should().Be(3);
        diag.MatchedLineCount.Should().Be(1);
        diag.DivergeLine.Should().Be(4);
        diag.FileLineAtDiverge.Should().Be("delta");
        diag.OldStringLineAtDiverge.Should().Be("delta_WRONG");
    }

    [Fact]
    public void BuildDiagnostic_WhitespaceMismatch_ReportsWhitespaceReason()
    {
        var fileContent = "    indented_with_spaces\n";
        var oldString = "\tindented_with_spaces\n";

        var diag = EditDiagnosticBuilder.BuildDiagnostic(fileContent, oldString);

        diag.Reason.Should().Be(EditMismatchReason.WhitespaceMismatch);
        diag.FormattedMessage.Should().Contain("WhitespaceMismatch");
        diag.FormattedMessage.Should().Contain("空白字符");
    }

    [Fact]
    public void BuildDiagnostic_SimilarFound_ReportsSimilarSnippetWithLineRange()
    {
        var fileContent = """
            public void Foo()
            {
                var x = 1;
                var y = 2;
                return x + y;
            }

            public void Bar()
            {
                var x = 1;
                var z = 3;
                return x + z;
            }
            """ + "\n";

        var oldString = """
            public void Baz()
            {
                var x = 1;
                var y = 2;
                return x + y;
            }
            """ + "\n";

        var diag = EditDiagnosticBuilder.BuildDiagnostic(fileContent, oldString);

        diag.Reason.Should().Be(EditMismatchReason.SimilarFound);
        diag.SimilarStartLine.Should().NotBeNull();
        diag.SimilarEndLine.Should().NotBeNull();
        diag.SimilarityScore.Should().BeGreaterThan(0.3);
        diag.SimilarSnippet.Should().NotBeNullOrEmpty();
        diag.FormattedMessage.Should().Contain("SimilarFound");
        diag.FormattedMessage.Should().Contain("Jaccard 相似度");
    }

    [Fact]
    public void BuildDiagnostic_EmptyOldString_ReturnsStringNotFound()
    {
        var fileContent = "some content\n";

        var diag = EditDiagnosticBuilder.BuildDiagnostic(fileContent, "");

        diag.Reason.Should().Be(EditMismatchReason.StringNotFound);
    }

    [Fact]
    public void BuildDiagnostic_LargeFile_SkipsSimilarityButStillReportsPartialMatch()
    {
        var sb = new StringBuilder();
        for (var i = 0; i < 6000; i++)
        {
            sb.Append($"line{i}\n");
        }

        sb.Append("target_line\n");
        sb.Append("diverge_here\n");
        var fileContent = sb.ToString();

        var oldString = "target_line\nWRONG_line\n";

        var diag = EditDiagnosticBuilder.BuildDiagnostic(fileContent, oldString);

        diag.Reason.Should().Be(EditMismatchReason.PartialMatch);
        diag.MatchedLine.Should().Be(6001);
    }

    [Fact]
    public void FormattedMessage_AlwaysContainsDiagnosticPrefix()
    {
        var fileContent = "hello\n";
        var oldString = "world\n";

        var diag = EditDiagnosticBuilder.BuildDiagnostic(fileContent, oldString);

        diag.FormattedMessage.Should().StartWith("String to replace not found in file.");
        diag.FormattedMessage.Should().Contain("[诊断]");
    }
}
