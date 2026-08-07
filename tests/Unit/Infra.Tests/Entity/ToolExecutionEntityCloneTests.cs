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

    [Fact]
    public void ToolExecutionEntity_Clone_MinimalFields_OnlyToolName()
    {
        var targetSession = new ObjectId(ObjectType.Session, "tgt-minimal");
        var source = new ToolExecutionEntity("web_fetch");
        try
        {
            var context = new CloneContext(targetSession);
            var cloned = (ToolExecutionEntity)source.Clone(context);

            cloned.ToolName.Should().Be("web_fetch");
            cloned.ToolUseId.Should().BeNull();
            cloned.SpanId.Should().BeNull();
            cloned.ArgumentsSummary.Should().BeNull();
            cloned.ResultSummary.Should().BeNull();
            cloned.IsError.Should().BeFalse();
            cloned.SessionObjectId.Should().BeNull();
            cloned.SessionId.Should().Be(targetSession);
            cloned.ObjectId.Should().NotBe(source.ObjectId);
            cloned.Dispose();
        }
        finally { source.Dispose(); }
    }

    [Fact]
    public void ToolExecutionEntity_Clone_SessionObjectId_Unmapped_BecomesNull()
    {
        var sourceSession = new ObjectId(ObjectType.Session, "src-unmapped");
        var targetSession = new ObjectId(ObjectType.Session, "tgt-unmapped");
        var unmappedSession = new ObjectId(ObjectType.Session, "never-cloned");

        var source = new ToolExecutionEntity("read_file", sessionId: sourceSession)
        {
            SessionObjectId = unmappedSession
        };
        try
        {
            var context = new CloneContext(targetSession);
            var cloned = (ToolExecutionEntity)source.Clone(context);

            cloned.SessionObjectId.Should().BeNull();
            cloned.Dispose();
        }
        finally { source.Dispose(); }
    }

    [Fact]
    public void ToolExecutionEntity_Clone_PreservesErrorState()
    {
        var targetSession = new ObjectId(ObjectType.Session, "tgt-err");
        var source = new ToolExecutionEntity("bash", toolUseId: "tu_err")
        {
            IsError = true,
            ResultSummary = "Command timed out",
            LifecycleState = EntityLifecycle.Completed,
            CompletedAt = DateTime.UtcNow,
        };
        try
        {
            var context = new CloneContext(targetSession);
            var cloned = (ToolExecutionEntity)source.Clone(context);

            cloned.IsError.Should().BeTrue();
            cloned.ResultSummary.Should().Be("Command timed out");
            cloned.Dispose();
        }
        finally { source.Dispose(); }
    }

    [Fact]
    public void ToolExecutionEntity_Clone_ModifyingClone_DoesNotAffectSource()
    {
        var targetSession = new ObjectId(ObjectType.Session, "tgt-indep");
        var source = new ToolExecutionEntity("grep")
        {
            ResultSummary = "original",
            IsError = false,
        };
        try
        {
            var context = new CloneContext(targetSession);
            var cloned = (ToolExecutionEntity)source.Clone(context);
            try
            {
                cloned.ResultSummary = "modified";
                cloned.IsError = true;
                cloned.LifecycleState = EntityLifecycle.Active;

                source.ResultSummary.Should().Be("original");
                source.IsError.Should().BeFalse();
                source.LifecycleState.Should().Be(EntityLifecycle.Created);
            }
            finally { cloned.Dispose(); }
        }
        finally { source.Dispose(); }
    }

    [Fact]
    public void ToolExecutionEntity_Clone_NestedCloneContext_ChainRemapping()
    {
        var sessionA = new ObjectId(ObjectType.Session, "session-a");
        var sessionB = new ObjectId(ObjectType.Session, "session-b");

        var source = new ToolExecutionEntity("bash", toolUseId: "tu_chain", sessionId: sessionA)
        {
            ResultSummary = "step1",
        };
        try
        {
            var contextA = new CloneContext(sessionA);
            var clonedA = (ToolExecutionEntity)source.Clone(contextA);
            try
            {
                contextA.Remap(source.ObjectId).Should().Be(clonedA.ObjectId);

                var contextB = new CloneContext(sessionB);
                var clonedB = (ToolExecutionEntity)clonedA.Clone(contextB);
                try
                {
                    contextB.Remap(clonedA.ObjectId).Should().Be(clonedB.ObjectId);
                    clonedB.ToolUseId.Should().Be("tu_chain");
                    clonedB.ResultSummary.Should().Be("step1");
                    clonedB.SessionId.Should().Be(sessionB);
                    clonedB.ObjectId.Should().NotBe(clonedA.ObjectId);
                    clonedB.ObjectId.Should().NotBe(source.ObjectId);
                }
                finally { clonedB.Dispose(); }
            }
            finally { clonedA.Dispose(); }
        }
        finally { source.Dispose(); }
    }

    [Fact]
    public async Task ToolExecutionEntity_Clone_Concurrent_ClonesAreIndependent()
    {
        var sourceSession = new ObjectId(ObjectType.Session, "src-concurrent");
        var source = new ToolExecutionEntity("bash", toolUseId: "tu_concurrent", sessionId: sourceSession)
        {
            ResultSummary = "concurrent-test",
        };
        try
        {
            var tasks = new List<Task<ToolExecutionEntity>>();
            for (var i = 0; i < 10; i++)
            {
                var targetSession = new ObjectId(ObjectType.Session, $"tgt-conc-{i}");
                tasks.Add(Task.Run(() =>
                {
                    var context = new CloneContext(targetSession);
                    return (ToolExecutionEntity)source.Clone(context);
                }));
            }

            var clones = await Task.WhenAll(tasks);
            try
            {
                var objectIds = clones.Select(c => c.ObjectId).ToHashSet();
                objectIds.Count.Should().Be(10);

                foreach (var clone in clones)
                {
                    clone.ToolUseId.Should().Be("tu_concurrent");
                    clone.ResultSummary.Should().Be("concurrent-test");
                    clone.Should().NotBeSameAs(source);
                }
            }
            finally
            {
                foreach (var clone in clones) clone.Dispose();
            }
        }
        finally { source.Dispose(); }
    }

    [Fact]
    public void BashProcessEntity_Clone_NullProcessIdAndCommand_PreservesDisplayName()
    {
        var targetSession = new ObjectId(ObjectType.Session, "tgt-null");
        var source = new BashProcessEntity(displayName: "empty-bash");
        try
        {
            source.DisplayName.Should().Be("empty-bash");

            var context = new CloneContext(targetSession);
            var cloned = (BashProcessEntity)source.Clone(context);

            cloned.ProcessId.Should().BeNull();
            cloned.Command.Should().BeNull();
            cloned.WorkingDirectory.Should().BeNull();
            cloned.DisplayName.Should().Be("empty-bash");
            cloned.ToolName.Should().Be("bash");
            cloned.Dispose();
        }
        finally { source.Dispose(); }
    }

    [Fact]
    public void BashProcessEntity_Clone_TimedOutStatus_Preserved()
    {
        var targetSession = new ObjectId(ObjectType.Session, "tgt-timeout");
        var source = new BashProcessEntity(
            processId: 77777,
            command: "sleep 999",
            sessionId: new ObjectId(ObjectType.Session, "src-timeout"))
        {
            Status = BashProcessStatus.TimedOut,
            ExitCode = null,
            IsError = true,
            ResultSummary = "Process timed out after 30s",
            LifecycleState = EntityLifecycle.Completed,
        };
        try
        {
            var context = new CloneContext(targetSession);
            var cloned = (BashProcessEntity)source.Clone(context);

            cloned.Status.Should().Be(BashProcessStatus.TimedOut);
            cloned.ExitCode.Should().BeNull();
            cloned.IsError.Should().BeTrue();
            cloned.ResultSummary.Should().Be("Process timed out after 30s");
            cloned.ProcessId.Should().Be(77777);
            cloned.Command.Should().Be("sleep 999");
            cloned.Dispose();
        }
        finally { source.Dispose(); }
    }

    [Fact]
    public void BashProcessEntity_Clone_KilledStatus_WithExitCode_Preserved()
    {
        var targetSession = new ObjectId(ObjectType.Session, "tgt-killed");
        var source = new BashProcessEntity(
            processId: 55555,
            command: "infinite-loop",
            sessionId: new ObjectId(ObjectType.Session, "src-killed"))
        {
            Status = BashProcessStatus.Killed,
            ExitCode = 137,
            IsError = true,
        };
        try
        {
            var context = new CloneContext(targetSession);
            var cloned = (BashProcessEntity)source.Clone(context);

            cloned.Status.Should().Be(BashProcessStatus.Killed);
            cloned.ExitCode.Should().Be(137);
            cloned.IsError.Should().BeTrue();
            cloned.Dispose();
        }
        finally { source.Dispose(); }
    }

    [Fact]
    public void ToolExecutionEntity_Clone_LifecycleStates_AllPreserved()
    {
        var targetSession = new ObjectId(ObjectType.Session, "tgt-lifecycle");
        var states = new[] { EntityLifecycle.Created, EntityLifecycle.Active, EntityLifecycle.Suspended, EntityLifecycle.Completed, EntityLifecycle.Persisted };

        foreach (var state in states)
        {
            var source = new ToolExecutionEntity("test") { LifecycleState = state };
            try
            {
                var context = new CloneContext(targetSession);
                var cloned = (ToolExecutionEntity)source.Clone(context);
                try
                {
                    cloned.LifecycleState.Should().Be(state);
                }
                finally { cloned.Dispose(); }
            }
            finally { source.Dispose(); }
        }
    }

    [Fact]
    public void WebFetchEntity_Clone_PreservesUrlAndHttpFields()
    {
        var targetSession = new ObjectId(ObjectType.Session, "tgt-web");
        var source = new WebFetchEntity(
            url: "https://example.com/api",
            toolUseId: "tu_web_001",
            spanId: "span_web",
            sessionId: new ObjectId(ObjectType.Session, "src-web"))
        {
            HttpStatusCode = 200,
            ContentLength = 1024,
            ResultSummary = "OK",
            LifecycleState = EntityLifecycle.Completed,
        };
        try
        {
            var context = new CloneContext(targetSession);
            var cloned = (WebFetchEntity)source.Clone(context);

            cloned.Url.Should().Be("https://example.com/api");
            cloned.HttpStatusCode.Should().Be(200);
            cloned.ContentLength.Should().Be(1024);
            cloned.ToolName.Should().Be("web_fetch");
            cloned.ResultSummary.Should().Be("OK");
            cloned.SessionId.Should().Be(targetSession);
            cloned.GetType().Should().Be(typeof(WebFetchEntity));
            cloned.Dispose();
        }
        finally { source.Dispose(); }
    }

    [Fact]
    public void UserInteractionEntity_Clone_PreservesQuestionAndResponse()
    {
        var targetSession = new ObjectId(ObjectType.Session, "tgt-interact");
        var source = new UserInteractionEntity(
            question: "Continue with deployment?",
            toolUseId: "tu_ask_001",
            sessionId: new ObjectId(ObjectType.Session, "src-interact"))
        {
            Response = "yes",
            LifecycleState = EntityLifecycle.Completed,
        };
        try
        {
            var context = new CloneContext(targetSession);
            var cloned = (UserInteractionEntity)source.Clone(context);

            cloned.Question.Should().Be("Continue with deployment?");
            cloned.Response.Should().Be("yes");
            cloned.ToolName.Should().Be("ask_user");
            cloned.GetType().Should().Be(typeof(UserInteractionEntity));
            cloned.Dispose();
        }
        finally { source.Dispose(); }
    }

    [Fact]
    public void SleepEntity_Clone_PreservesDurationAndProgress()
    {
        var targetSession = new ObjectId(ObjectType.Session, "tgt-sleep");
        var source = new SleepEntity(
            durationSeconds: 30,
            reason: "rate-limit-backoff",
            toolUseId: "tu_sleep_001",
            sessionId: new ObjectId(ObjectType.Session, "src-sleep"))
        {
            RemainingSeconds = 12,
            TickCount = 18,
            LifecycleState = EntityLifecycle.Active,
        };
        try
        {
            var context = new CloneContext(targetSession);
            var cloned = (SleepEntity)source.Clone(context);

            cloned.DurationSeconds.Should().Be(30);
            cloned.RemainingSeconds.Should().Be(12);
            cloned.TickCount.Should().Be(18);
            cloned.Reason.Should().Be("rate-limit-backoff");
            cloned.ToolName.Should().Be("sleep");
            cloned.GetType().Should().Be(typeof(SleepEntity));
            cloned.Dispose();
        }
        finally { source.Dispose(); }
    }

    [Fact]
    public void ReplSessionEntity_Clone_PreservesLanguageAndEnabled()
    {
        var targetSession = new ObjectId(ObjectType.Session, "tgt-repl");
        var source = new ReplSessionEntity(
            language: "python",
            toolUseId: "tu_repl_001",
            sessionId: new ObjectId(ObjectType.Session, "src-repl"))
        {
            IsEnabled = true,
            LifecycleState = EntityLifecycle.Active,
        };
        try
        {
            var context = new CloneContext(targetSession);
            var cloned = (ReplSessionEntity)source.Clone(context);

            cloned.Language.Should().Be("python");
            cloned.IsEnabled.Should().BeTrue();
            cloned.ToolName.Should().Be("repl");
            cloned.GetType().Should().Be(typeof(ReplSessionEntity));
            cloned.Dispose();
        }
        finally { source.Dispose(); }
    }
}
