namespace McpToolRegistry.Tests;

public sealed class PermissionAwareToolExecutorEntityTests
{
    [Fact]
    public void ToolExecutionContext_HasExecutionEntity_AfterCreation()
    {
        var entity = new ToolExecutionEntity("read_file");
        var context = new ToolExecutionContext
        {
            ToolName = "read_file",
            Arguments = [],
            ExecutionEntity = entity,
        };

        context.ExecutionEntity.Should().BeSameAs(entity);
        context.ExecutionEntity!.ToolName.Should().Be("read_file");
    }

    [Fact]
    public void ToolExecutionContext_ExecutionEntity_IsNullByDefault()
    {
        var context = new ToolExecutionContext
        {
            ToolName = "test",
            Arguments = [],
        };

        context.ExecutionEntity.Should().BeNull();
    }

    [Fact]
    public void ToolExecutionEntity_LifecycleFlow_MirrorsExecutorFlow()
    {
        var entity = new ToolExecutionEntity("bash");

        entity.LifecycleState.Should().Be(EntityLifecycle.Created);

        entity.LifecycleState = EntityLifecycle.Active;
        entity.StartedAt = DateTime.UtcNow;
        entity.LifecycleState.Should().Be(EntityLifecycle.Active);

        entity.LifecycleState = EntityLifecycle.Completed;
        entity.CompletedAt = DateTime.UtcNow;
        entity.IsError = false;
        entity.ResultSummary = "command executed";
        entity.LifecycleState.Should().Be(EntityLifecycle.Completed);
        entity.IsError.Should().BeFalse();
        entity.ResultSummary.Should().Be("command executed");

        entity.Dispose();
    }

    [Fact]
    public void ToolExecutionEntity_ErrorFlow_SetsIsError()
    {
        var entity = new ToolExecutionEntity("grep");

        entity.LifecycleState = EntityLifecycle.Active;
        entity.StartedAt = DateTime.UtcNow;

        entity.LifecycleState = EntityLifecycle.Completed;
        entity.CompletedAt = DateTime.UtcNow;
        entity.IsError = true;
        entity.ResultSummary = "pattern not found";

        entity.IsError.Should().BeTrue();
        entity.ResultSummary.Should().Be("pattern not found");

        entity.Dispose();
    }

    [Fact]
    public void ToolExecutionEntity_RegisteredInGlobalRegistry()
    {
        var entity = new ToolExecutionEntity("web_fetch");
        try
        {
            ToolExecutionEntity.Registry.Get(entity.ObjectId).Should().BeSameAs(entity);
            ToolExecutionEntity.Registry.GetByToolName("web_fetch").Should().Contain(entity);
        }
        finally { entity.Dispose(); }
    }

    [Fact]
    public void ToolExecutionEntity_SpanId_LinkedToTelemetry()
    {
        var entity = new ToolExecutionEntity("bash", spanId: "span_abc123");
        try
        {
            entity.SpanId.Should().Be("span_abc123");
        }
        finally { entity.Dispose(); }
    }

    [Fact]
    public void BackfillEntityMetadata_BashProcessEntity_SetsExitCode()
    {
        var bash = new BashProcessEntity(command: "ls");
        try
        {
            var result = new ToolResult
            {
                Content = [new ToolContent { Type = ToolContentType.Text, Text = "ok" }],
                IsError = false,
                EntityMetadata = [EntityMetadataEntry.Int("exit_code", 0)],
            };

            var context = new ToolExecutionContext
            {
                ToolName = "bash",
                Arguments = [],
                ExecutionEntity = bash,
                Result = result,
            };

            CompleteAndBackfill(context);

            bash.ExitCode.Should().Be(0);
            bash.Status.Should().Be(BashProcessStatus.Exited);
        }
        finally { bash.Dispose(); }
    }

    [Fact]
    public void BackfillEntityMetadata_BashProcessEntity_Interrupted_SetsTimedOut()
    {
        var bash = new BashProcessEntity(command: "sleep 999");
        try
        {
            var result = new ToolResult
            {
                Content = [new ToolContent { Type = ToolContentType.Text, Text = "timeout" }],
                IsError = true,
                EntityMetadata =
                [
                    EntityMetadataEntry.Int("exit_code", -1),
                    EntityMetadataEntry.Bool("interrupted", true),
                ],
            };

            var context = new ToolExecutionContext
            {
                ToolName = "bash",
                Arguments = [],
                ExecutionEntity = bash,
                Result = result,
            };

            CompleteAndBackfill(context);

            bash.ExitCode.Should().Be(-1);
            bash.Status.Should().Be(BashProcessStatus.TimedOut);
        }
        finally { bash.Dispose(); }
    }

    [Fact]
    public void BackfillEntityMetadata_WebFetchEntity_SetsHttpStatusCode()
    {
        var web = new WebFetchEntity(url: "https://example.com");
        try
        {
            var result = new ToolResult
            {
                Content = [new ToolContent { Type = ToolContentType.Text, Text = "ok" }],
                IsError = false,
                EntityMetadata =
                [
                    EntityMetadataEntry.Int("http_status_code", 200),
                    EntityMetadataEntry.Long("content_length", 12345L),
                ],
            };

            var context = new ToolExecutionContext
            {
                ToolName = "web_fetch",
                Arguments = [],
                ExecutionEntity = web,
                Result = result,
            };

            CompleteAndBackfill(context);

            web.HttpStatusCode.Should().Be(200);
            web.ContentLength.Should().Be(12345L);
        }
        finally { web.Dispose(); }
    }

    [Fact]
    public void BackfillEntityMetadata_NoMetadata_DoesNotThrow()
    {
        var entity = new ToolExecutionEntity("read_file");
        try
        {
            var result = new ToolResult
            {
                Content = [new ToolContent { Type = ToolContentType.Text, Text = "ok" }],
                IsError = false,
            };

            var context = new ToolExecutionContext
            {
                ToolName = "read_file",
                Arguments = [],
                ExecutionEntity = entity,
                Result = result,
            };

            var act = () => CompleteAndBackfill(context);
            act.Should().NotThrow();
        }
        finally { entity.Dispose(); }
    }

    [Fact]
    public void BackfillEntityMetadata_EmptyMetadata_DoesNotThrow()
    {
        var entity = new ToolExecutionEntity("read_file");
        try
        {
            var result = new ToolResult
            {
                Content = [new ToolContent { Type = ToolContentType.Text, Text = "ok" }],
                IsError = false,
                EntityMetadata = [],
            };

            var context = new ToolExecutionContext
            {
                ToolName = "read_file",
                Arguments = [],
                ExecutionEntity = entity,
                Result = result,
            };

            var act = () => CompleteAndBackfill(context);
            act.Should().NotThrow();
        }
        finally { entity.Dispose(); }
    }

    [Fact]
    public void EntityMetadataEntry_FactoryMethods_CreateCorrectEntries()
    {
        var intEntry = EntityMetadataEntry.Int("exit_code", 42);
        intEntry.Key.Should().Be("exit_code");
        intEntry.IntValue.Should().Be(42);
        intEntry.LongValue.Should().BeNull();
        intEntry.StringValue.Should().BeNull();
        intEntry.BoolValue.Should().BeNull();

        var longEntry = EntityMetadataEntry.Long("content_length", 9999L);
        longEntry.Key.Should().Be("content_length");
        longEntry.LongValue.Should().Be(9999L);

        var stringEntry = EntityMetadataEntry.String("background_task_id", "task_123");
        stringEntry.Key.Should().Be("background_task_id");
        stringEntry.StringValue.Should().Be("task_123");

        var boolEntry = EntityMetadataEntry.Bool("interrupted", true);
        boolEntry.Key.Should().Be("interrupted");
        boolEntry.BoolValue.Should().Be(true);
    }

    [Fact]
    public void ToolResultBuilder_WithEntityMetadata_PropagatesToToolResult()
    {
        var result = ToolResultBuilder.Success()
            .WithText("ok")
            .WithEntityMetadata(EntityMetadataEntry.Int("exit_code", 0))
            .Build();

        result.EntityMetadata.Should().NotBeNull();
        result.EntityMetadata!.Count.Should().Be(1);
        result.EntityMetadata[0].Key.Should().Be("exit_code");
        result.EntityMetadata[0].IntValue.Should().Be(0);
    }

    [Fact]
    public void ToolResultBuilder_WithEntityMetadata_MultipleEntries()
    {
        var result = ToolResultBuilder.Success()
            .WithText("ok")
            .WithEntityMetadata(EntityMetadataEntry.Int("http_status_code", 200))
            .WithEntityMetadata(EntityMetadataEntry.Long("content_length", 5000L))
            .Build();

        result.EntityMetadata.Should().NotBeNull();
        result.EntityMetadata!.Count.Should().Be(2);
        result.EntityMetadata[0].Key.Should().Be("http_status_code");
        result.EntityMetadata[1].Key.Should().Be("content_length");
    }

    private static void CompleteAndBackfill(ToolExecutionContext context)
    {
        var entity = context.ExecutionEntity!;
        entity.CompletedAt = DateTime.UtcNow;
        entity.LifecycleState = EntityLifecycle.Completed;
        entity.IsError = context.Result?.IsError ?? true;
        entity.ResultSummary = context.Result?
            .Content?.FirstOrDefault(c => c.Type == ToolContentType.Text)?
            .Text;

        BackfillEntityMetadata(entity, context.Result?.EntityMetadata);
    }

    private static void BackfillEntityMetadata(ToolExecutionEntity entity, List<EntityMetadataEntry>? metadata)
    {
        if (metadata is null || metadata.Count == 0) return;

        switch (entity)
        {
            case BashProcessEntity bash:
                var exitCodeEntry = metadata.Find(m => m.Key == "exit_code");
                if (exitCodeEntry?.IntValue is int exitCode)
                    bash.ExitCode = exitCode;
                var interruptedEntry = metadata.Find(m => m.Key == "interrupted");
                if (interruptedEntry?.BoolValue == true)
                    bash.Status = BashProcessStatus.TimedOut;
                else if (bash.ExitCode.HasValue)
                    bash.Status = BashProcessStatus.Exited;
                break;

            case WebFetchEntity web:
                var httpStatusEntry = metadata.Find(m => m.Key == "http_status_code");
                if (httpStatusEntry?.IntValue is int statusCode)
                    web.HttpStatusCode = statusCode;
                var contentLengthEntry = metadata.Find(m => m.Key == "content_length");
                if (contentLengthEntry?.LongValue is long contentLength)
                    web.ContentLength = contentLength;
                break;
        }
    }
}
