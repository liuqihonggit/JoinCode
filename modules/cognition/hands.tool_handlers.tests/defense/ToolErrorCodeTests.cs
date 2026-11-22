namespace Core.Tests;

/// <summary>
/// TASK125 — 权限拒绝必须靠结构化错误码判定，不能靠结果文本里的「拒绝」二字。
/// 这里锁三件事：码集合口径（PR002 不算拒绝）、错误构建器把码带进 ToolResult、判定入口唯一。
/// </summary>
public sealed class ToolErrorCodeTests {
    [Theory]
    [InlineData("PD001")]
    [InlineData("PD002")]
    [InlineData("PD00B")]
    [InlineData("PD003")]
    [InlineData("PD004")]
    [InlineData("PD005")]
    public void IsPermissionDenial_AllDeniedByCodes_AreDenials(string code) {
        ToolErrorCodes.IsPermissionDenial(code).Should().BeTrue();
    }

    [Theory]
    [InlineData("PR001", "权限待确认不算拒绝")]
    [InlineData("PR002", "待确认不是拒绝，判定为拒绝会把正常询问算成 denial")]
    [InlineData("SV001", "schema 校验失败不是权限问题")]
    [InlineData("EF002", "超时不是权限问题")]
    [InlineData(null, "无错误码时由调用方决定是否文本兜底")]
    [InlineData("", "空码不是拒绝")]
    public void IsPermissionDenial_NonDenialCodes_AreNotDenials(string? code, string reason) {
        ToolErrorCodes.IsPermissionDenial(code).Should().BeFalse(reason);
    }

    [Fact]
    public void ToolResultBuilder_ErrorPath_CarriesStructuredErrorCode() {
        var result = ToolResultBuilder
            .Error()
            .WithText("用户拒绝工具执行: bash")
            .WithErrorType(ToolErrorType.PermissionDenied, ToolErrorCode.PermissionDeniedByUser)
            .Build();

        result.IsError.Should().BeTrue();
        result.ErrorCode.Should().Be("PD002", "消费方要靠 ToolResult.ErrorCode 判定，不能再读文本");
        ToolErrorCodes.IsPermissionDenial(result.ErrorCode).Should().BeTrue();
    }

    [Fact]
    public void ToolErrorJsonBuilder_ToToolResult_CarriesStructuredErrorCode() {
        var result = ToolErrorJsonBuilder.ToToolResult(
            ToolErrorType.PermissionDenied,
            "用户拒绝工具执行: bash",
            "bash",
            ToolErrorCode.PermissionDeniedByUser);

        result.ErrorCode.Should().Be("PD002");
    }

    [Fact]
    public void ToolResultBuilder_SuccessPath_HasNoErrorCode() {
        var result = ToolResultBuilder.Success().WithText("ok").Build();

        result.ErrorCode.Should().BeNull();
        ToolErrorCodes.IsPermissionDenial(result.ErrorCode).Should().BeFalse();
    }
}
