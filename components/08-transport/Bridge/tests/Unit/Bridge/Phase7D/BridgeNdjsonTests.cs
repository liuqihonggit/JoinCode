
namespace Bridge.Tests.Phase7D;

public sealed partial class BridgeMainTests
{
    #region P2-5: NDJSON 结构化解析 — ExtractActivities

    [Fact]
    public void ExtractActivities_AssistantToolUse_ReturnsToolStart()
    {
        var ndjson = """{"type":"assistant","message":{"content":[{"type":"tool_use","name":"Read","input":{"file_path":"/src/foo.cs"}}]}}""";
        var activities = BridgeNdjsonParser.ExtractActivities(ndjson);
        Assert.Single(activities);
        Assert.Equal(BridgeNdjsonActivityType.ToolStart, activities[0].Type);
        Assert.Equal("Reading /src/foo.cs", activities[0].Summary);
    }

    [Fact]
    public void ExtractActivities_AssistantText_ReturnsTextActivity()
    {
        var ndjson = """{"type":"assistant","message":{"content":[{"type":"text","text":"Hello world"}]}}""";
        var activities = BridgeNdjsonParser.ExtractActivities(ndjson);
        Assert.Single(activities);
        Assert.Equal(BridgeNdjsonActivityType.Text, activities[0].Type);
        Assert.Equal("Hello world", activities[0].Summary);
    }

    [Fact]
    public void ExtractActivities_ResultSuccess_ReturnsResultActivity()
    {
        var ndjson = """{"type":"result","subtype":"success"}""";
        var activities = BridgeNdjsonParser.ExtractActivities(ndjson);
        Assert.Single(activities);
        Assert.Equal(BridgeNdjsonActivityType.Result, activities[0].Type);
        Assert.Equal("Session completed", activities[0].Summary);
    }

    [Fact]
    public void ExtractActivities_ResultError_ReturnsErrorActivity()
    {
        var ndjson = """{"type":"result","subtype":"error","errors":["Permission denied"]}""";
        var activities = BridgeNdjsonParser.ExtractActivities(ndjson);
        Assert.Single(activities);
        Assert.Equal(BridgeNdjsonActivityType.Error, activities[0].Type);
        Assert.Equal("Permission denied", activities[0].Summary);
    }

    [Fact]
    public void ExtractActivities_ResultErrorNoErrors_UsesSubtype()
    {
        var ndjson = """{"type":"result","subtype":"timeout"}""";
        var activities = BridgeNdjsonParser.ExtractActivities(ndjson);
        Assert.Single(activities);
        Assert.Equal(BridgeNdjsonActivityType.Error, activities[0].Type);
        Assert.Equal("Error: timeout", activities[0].Summary);
    }

    [Fact]
    public void ExtractActivities_InvalidJson_ReturnsEmpty()
    {
        var activities = BridgeNdjsonParser.ExtractActivities("not json");
        Assert.Empty(activities);
    }

    [Fact]
    public void ExtractActivities_EmptyString_ReturnsEmpty()
    {
        var activities = BridgeNdjsonParser.ExtractActivities("");
        Assert.Empty(activities);
    }

    [Fact]
    public void ExtractActivities_UserType_ReturnsEmpty()
    {
        var ndjson = """{"type":"user","content":"Hello"}""";
        var activities = BridgeNdjsonParser.ExtractActivities(ndjson);
        Assert.Empty(activities);
    }

    [Fact]
    public void ExtractActivities_MultipleToolUse_ReturnsMultipleActivities()
    {
        var ndjson = """{"type":"assistant","message":{"content":[{"type":"tool_use","name":"Read","input":{"file_path":"/a.cs"}},{"type":"tool_use","name":"Bash","input":{"command":"ls"}}]}}""";
        var activities = BridgeNdjsonParser.ExtractActivities(ndjson);
        Assert.Equal(2, activities.Count);
        Assert.Equal("Reading /a.cs", activities[0].Summary);
        Assert.Equal("Running ls", activities[1].Summary);
    }

    #endregion

    #region P2-5: NDJSON 结构化解析 — ExtractPermissionRequest

    [Fact]
    public void ExtractPermissionRequest_ControlRequest_ReturnsRequest()
    {
        var ndjson = """{"type":"control_request","request_id":"req-123","request":{"subtype":"can_use_tool","tool_name":"Bash","input":{"command":"rm -rf /"},"tool_use_id":"tu-456"}}""";
        var permReq = BridgeNdjsonParser.ExtractPermissionRequest(ndjson);
        Assert.NotNull(permReq);
        Assert.Equal("control_request", permReq.Type);
        Assert.Equal("req-123", permReq.RequestId);
        Assert.Equal("Bash", permReq.ToolName);
        Assert.Equal("tu-456", permReq.ToolUseId);
        Assert.True(permReq.Input.ContainsKey("command"));
    }

    [Fact]
    public void ExtractPermissionRequest_NonControlRequest_ReturnsNull()
    {
        var ndjson = """{"type":"assistant","content":"hello"}""";
        var permReq = BridgeNdjsonParser.ExtractPermissionRequest(ndjson);
        Assert.Null(permReq);
    }

    [Fact]
    public void ExtractPermissionRequest_NonCanUseTool_ReturnsNull()
    {
        var ndjson = """{"type":"control_request","request_id":"req-123","request":{"subtype":"initialize"}}""";
        var permReq = BridgeNdjsonParser.ExtractPermissionRequest(ndjson);
        Assert.Null(permReq);
    }

    [Fact]
    public void ExtractPermissionRequest_InvalidJson_ReturnsNull()
    {
        var permReq = BridgeNdjsonParser.ExtractPermissionRequest("not json");
        Assert.Null(permReq);
    }

    [Fact]
    public void ExtractPermissionRequest_EmptyString_ReturnsNull()
    {
        var permReq = BridgeNdjsonParser.ExtractPermissionRequest("");
        Assert.Null(permReq);
    }

    #endregion

    #region P2-5: NDJSON 结构化解析 — ToolSummary

    [Fact]
    public void ToolSummary_ReadWithFilePath_ReturnsReadingPath()
    {
        var input = new Dictionary<string, JsonElement>
        {
            ["file_path"] = JsonDocument.Parse("\"/src/foo.cs\"").RootElement,
        };
        var summary = BridgeNdjsonParser.ToolSummary("Read", input);
        Assert.Equal("Reading /src/foo.cs", summary);
    }

    [Fact]
    public void ToolSummary_BashWithCommand_ReturnsRunningCommand()
    {
        var input = new Dictionary<string, JsonElement>
        {
            ["command"] = JsonDocument.Parse("\"ls -la\"").RootElement,
        };
        var summary = BridgeNdjsonParser.ToolSummary("Bash", input);
        Assert.Equal("Running ls -la", summary);
    }

    [Fact]
    public void ToolSummary_UnknownTool_ReturnsUsingToolName()
    {
        var input = new Dictionary<string, JsonElement>();
        var summary = BridgeNdjsonParser.ToolSummary("CustomTool", input);
        Assert.Equal("Using CustomTool", summary);
    }

    [Fact]
    public void ToolSummary_LongPath_TruncatesTo80Chars()
    {
        var longPath = new string('a', 100);
        var input = new Dictionary<string, JsonElement>
        {
            ["file_path"] = JsonDocument.Parse($"\"{longPath}\"").RootElement,
        };
        var summary = BridgeNdjsonParser.ToolSummary("Read", input);
        Assert.True(summary.Length <= 80);
    }

    #endregion

    #region P2-5: NDJSON 结构化解析 — BridgeMainDeps callbacks

    [Fact]
    public void BridgeMainDeps_OnPermissionRequest_CanBeSet()
    {
        var deps = BridgeTestHelperMethods.CreateDeps();
        deps.OnPermissionRequest = (_, _, _) => { };
        Assert.NotNull(deps.OnPermissionRequest);
    }

    [Fact]
    public void BridgeMainDeps_OnActivity_CanBeSet()
    {
        var deps = BridgeTestHelperMethods.CreateDeps();
        deps.OnActivity = (_, _) => { };
        Assert.NotNull(deps.OnActivity);
    }

    [Fact]
    public void BridgeMainDeps_NdjsonCallbacks_DefaultNull()
    {
        var deps = BridgeTestHelperMethods.CreateDeps();
        Assert.Null(deps.OnPermissionRequest);
        Assert.Null(deps.OnActivity);
    }

    [Fact]
    public void BridgeSubprocessOptions_OnPermissionRequest_CanBeSet()
    {
        Action<BridgePermissionRequest, string?>? callback = (_, _) => { };
        var opts = new BridgeSubprocessOptions
        {
            SessionId = "test",
            OnPermissionRequest = callback,
        };
        Assert.NotNull(opts.OnPermissionRequest);
    }

    [Fact]
    public void BridgeSubprocessOptions_OnActivity_CanBeSet()
    {
        Action<BridgeNdjsonActivity>? callback = _ => { };
        var opts = new BridgeSubprocessOptions
        {
            SessionId = "test",
            OnActivity = callback,
        };
        Assert.NotNull(opts.OnActivity);
    }

    #endregion
}
