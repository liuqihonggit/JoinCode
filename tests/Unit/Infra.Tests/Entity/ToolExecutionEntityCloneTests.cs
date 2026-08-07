namespace Infra.Tests.EntityTests;

public sealed class ToolExecutionEntityCloneTests
{
    [Fact]
    public void ToolExecutionEntity_Clone_WithValidContext_ReturnsClonedEntity()
    {
        var sourceSession = new ObjectId(ObjectType.Session, "source-session");
        var targetSession = new ObjectId(ObjectType.Session, "target-session");

        var source = new ToolExecutionEntity(
            "bash",
            toolUseId: "tu_001",
            spanId: "span_001",
            displayName: "bash-exec",
            sessionId: sourceSession)
        {
            ArgumentsSummary = "ls -la",
            ResultSummary = "total 42",
            IsError = false,
            SessionObjectId = sourceSession,
            LifecycleState = EntityLifecycle.Completed,
            StartedAt = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc),
            CompletedAt = new DateTime(2026, 1, 1, 10, 0, 5, DateTimeKind.Utc),
        };
        try
        {
            var context = new CloneContext(targetSession);
            var cloned = (ToolExecutionEntity)source.Clone(context);

            cloned.Should().NotBeNull();
            cloned.Should().NotBeSameAs(source);
            cloned.ObjectId.Should().NotBe(source.ObjectId);
            cloned.SessionId.Should().Be(targetSession);
            cloned.ToolName.Should().Be("bash");
            cloned.ToolUseId.Should().Be("tu_001");
            cloned.SpanId.Should().Be("span_001");
            cloned.DisplayName.Should().Be("bash-exec");
            cloned.ArgumentsSummary.Should().Be("ls -la");
            cloned.ResultSummary.Should().Be("total 42");
            cloned.IsError.Should().BeFalse();
            cloned.LifecycleState.Should().Be(EntityLifecycle.Completed);
            cloned.StartedAt.Should().Be(source.StartedAt);
            cloned.CompletedAt.Should().Be(source.CompletedAt);
            context.Remap(source.ObjectId).Should().Be(cloned.ObjectId);
            cloned.Dispose();
        }
        finally { source.Dispose(); }
    }

    [Fact]
    public void ToolExecutionEntity_Clone_SessionObjectId_RemappedToTarget()
    {
        var sourceSession = new ObjectId(ObjectType.Session, "src");
        var targetSession = new ObjectId(ObjectType.Session, "tgt");
        var mappedSessionObjectId = new ObjectId(ObjectType.Session, "mapped-session");

        var source = new ToolExecutionEntity("read_file", sessionId: sourceSession)
        {
            SessionObjectId = sourceSession
        };
        try
        {
            var context = new CloneContext(targetSession);
            context.Map(sourceSession, mappedSessionObjectId);

            var cloned = (ToolExecutionEntity)source.Clone(context);
            cloned.SessionObjectId.Should().Be(mappedSessionObjectId);
            cloned.Dispose();
        }
        finally { source.Dispose(); }
    }

    [Fact]
    public void ToolExecutionEntity_Clone_RegistersInTargetSessionScope()
    {
        var sourceSession = new ObjectId(ObjectType.Session, "src-scope");
        var targetSession = new ObjectId(ObjectType.Session, "tgt-scope");

        var source = new ToolExecutionEntity("grep", sessionId: sourceSession);
        try
        {
            var context = new CloneContext(targetSession);
            var cloned = (ToolExecutionEntity)source.Clone(context);

            SessionRouter.TryGetScope(targetSession, out var scope).Should().BeTrue();
            scope!.Resolve<ToolExecutionEntity>(cloned.ObjectId).Should().BeSameAs(cloned);
            cloned.Dispose();
        }
        finally { source.Dispose(); }
    }

    [Fact]
    public void BashProcessEntity_Clone_PreservesProcessSpecificFields()
    {
        var sourceSession = new ObjectId(ObjectType.Session, "src-bash");
        var targetSession = new ObjectId(ObjectType.Session, "tgt-bash");

        var source = new BashProcessEntity(
            processId: 12345,
            command: "dotnet build",
            workingDirectory: "/home/user/project",
            toolUseId: "tu_bash_001",
            spanId: "span_bash_001",
            displayName: "bash-build",
            sessionId: sourceSession)
        {
            ArgumentsSummary = "dotnet build -c Release",
            ResultSummary = "Build succeeded",
            IsError = false,
            ExitCode = 0,
            Status = BashProcessStatus.Exited,
            LifecycleState = EntityLifecycle.Completed,
            CompletedAt = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
        };
        try
        {
            var context = new CloneContext(targetSession);
            var cloned = (BashProcessEntity)source.Clone(context);

            cloned.Should().NotBeSameAs(source);
            cloned.SessionId.Should().Be(targetSession);
            cloned.ProcessId.Should().Be(12345);
            cloned.Command.Should().Be("dotnet build");
            cloned.WorkingDirectory.Should().Be("/home/user/project");
            cloned.ToolUseId.Should().Be("tu_bash_001");
            cloned.SpanId.Should().Be("span_bash_001");
            cloned.DisplayName.Should().Be("bash-build");
            cloned.ExitCode.Should().Be(0);
            cloned.Status.Should().Be(BashProcessStatus.Exited);
            cloned.ArgumentsSummary.Should().Be("dotnet build -c Release");
            cloned.ResultSummary.Should().Be("Build succeeded");
            cloned.IsError.Should().BeFalse();
            cloned.LifecycleState.Should().Be(EntityLifecycle.Completed);
            cloned.ObjectId.Type.Should().Be(ObjectType.ShellCommand);
            context.Remap(source.ObjectId).Should().Be(cloned.ObjectId);
            cloned.Dispose();
        }
        finally { source.Dispose(); }
    }

    [Fact]
    public void BashProcessEntity_Clone_RunningState_PreservesNullExitCode()
    {
        var sourceSession = new ObjectId(ObjectType.Session, "src-running");
        var targetSession = new ObjectId(ObjectType.Session, "tgt-running");

        var source = new BashProcessEntity(
            processId: 99999,
            command: "long-running-task",
            sessionId: sourceSession)
        {
            Status = BashProcessStatus.Running,
            LifecycleState = EntityLifecycle.Active,
        };
        try
        {
            var context = new CloneContext(targetSession);
            var cloned = (BashProcessEntity)source.Clone(context);

            cloned.ProcessId.Should().Be(99999);
            cloned.Command.Should().Be("long-running-task");
            cloned.Status.Should().Be(BashProcessStatus.Running);
            cloned.ExitCode.Should().BeNull();
            cloned.LifecycleState.Should().Be(EntityLifecycle.Active);
            cloned.Dispose();
        }
        finally { source.Dispose(); }
    }

    [Fact]
    public void BashProcessEntity_Clone_RegistersInToolExecutionRegistry()
    {
        var sourceSession = new ObjectId(ObjectType.Session, "src-reg");
        var targetSession = new ObjectId(ObjectType.Session, "tgt-reg");

        var source = new BashProcessEntity(command: "echo hello", sessionId: sourceSession);
        try
        {
            var context = new CloneContext(targetSession);
            var cloned = (BashProcessEntity)source.Clone(context);

            ToolExecutionEntity.Registry.Get(cloned.ObjectId).Should().BeSameAs(cloned);
            cloned.Dispose();
            ToolExecutionEntity.Registry.Get(cloned.ObjectId).Should().BeNull();
        }
        finally { source.Dispose(); }
    }
}
