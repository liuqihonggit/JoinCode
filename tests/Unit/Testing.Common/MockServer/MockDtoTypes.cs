
namespace Testing.Common.MockServer;

public sealed class MockChatCompletionResponse
{
    public string Id { get; init; } = string.Empty;
    public string Object { get; init; } = string.Empty;
    public long Created { get; init; }
    public string Model { get; init; } = string.Empty;
    public List<MockChatChoice> Choices { get; init; } = new();
    public MockUsage? Usage { get; init; }
}

public sealed class MockChatChoice
{
    public int Index { get; init; }
    public MockApiMessage? Message { get; init; }
    public MockChatDelta? Delta { get; init; }
    public string? FinishReason { get; init; }
}

public sealed class MockApiMessage
{
    public string Role { get; init; } = string.Empty;
    public string? Content { get; init; }
    public List<MockToolCall>? ToolCalls { get; init; }
}

public sealed class MockToolCall
{
    public string Id { get; init; } = string.Empty;
    public string Type { get; init; } = "function";
    public MockToolCallFunction Function { get; init; } = new();
}

public sealed class MockToolCallFunction
{
    public string Name { get; init; } = string.Empty;
    public string Arguments { get; init; } = "{}";
}

public sealed class MockChatDelta
{
    public string? Content { get; init; }
    public List<MockToolCallDelta>? ToolCalls { get; init; }
}

public sealed class MockToolCallDelta
{
    public int Index { get; init; }
    public string? Id { get; init; }
    public string? Type { get; init; }
    public MockToolCallFunctionDelta? Function { get; init; }
}

public sealed class MockToolCallFunctionDelta
{
    public string? Name { get; init; }
    public string? Arguments { get; init; }
}

public sealed class MockUsage
{
    public int PromptTokens { get; init; }
    public int CompletionTokens { get; init; }
    public int TotalTokens { get; init; }
}

public sealed class MockModelsResponse
{
    public string Object { get; init; } = "list";
    public List<MockModelItem> Data { get; init; } = new();
}

public sealed class MockModelItem
{
    public string Id { get; init; } = string.Empty;
    public string Object { get; init; } = "model";
    public long Created { get; init; }
    public string OwnedBy { get; init; } = string.Empty;
}
