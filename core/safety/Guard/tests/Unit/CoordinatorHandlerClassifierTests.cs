using Core.Hooks.ToolPermission.Handlers;
using Core.Security.Services;
using HookPermissionUpdate = JoinCode.Abstractions.Hooks.PermissionUpdate;

namespace JoinCode.Tests.Guard;

public class CoordinatorHandlerClassifierTests
{
    private sealed class StubCommandClassifier : ICommandClassifier
    {
        private readonly CommandClassification _result;
        public StubCommandClassifier(CommandClassification result) => _result = result;
        public CommandClassification Classify(ShellCommand command, string workingDirectory) => _result;
    }

    private sealed class StubAutoModeClassifier : IAutoModeClassifier
    {
        private readonly ClassificationResult _result;
        public StubAutoModeClassifier(ClassificationResult result) => _result = result;
        public Task<ClassificationResult> ClassifyAsync(ClassificationRequest request, CancellationToken ct = default) => Task.FromResult(_result);
    }

    private sealed class StubPermissionLogger : IPermissionLogger
    {
        public void LogPermissionDecision(PermissionLogContext context, PermissionDecisionArgs args) { }
        public void LogPermissionCancelled(PermissionLogContext context) { }
        public void LogCodeEditToolDecision(string toolName, string decision, string source, string? language = null) { }
    }

    private sealed class StubQueueOps : IPermissionQueueOperations
    {
        public readonly List<PermissionQueueItem> Items = [];
        public void Push(PermissionQueueItem item) => Items.Add(item);
        public void Remove(string toolUseId) { }
        public void Update(string toolUseId, Action<PermissionQueueItem> patch) { }
    }

    private static Dictionary<string, JsonElement> MakeInput(string command)
    {
        return new Dictionary<string, JsonElement>
        {
            ["command"] = JsonSerializer.Deserialize<JsonElement>($"\"{command}\"")
        };
    }

    [Fact]
    public async Task HandleAsync_ReadOnlyCommand_ClassifierAutoApproves()
    {
        var classifier = new StubCommandClassifier(
            new CommandClassification(CommandCategory.ReadOnly, []));
        var queueOps = new StubQueueOps();
        var ctx = new PermissionContext(
            "bash", MakeInput("ls -la"), "msg1", "tool1",
            new StubPermissionLogger(), queueOps);

        var handler = new CoordinatorHandler();
        var result = await handler.HandleAsync(new CoordinatorPermissionParams
        {
            Context = ctx,
            PendingClassifierCheck = new object(),
            Classifier = classifier,
            HookExecutor = new StubHookExecutor()
        }).ConfigureAwait(true);

        Assert.NotNull(result);
        Assert.Equal(PermissionBehavior.Allow, result.Behavior);
    }

    [Fact]
    public async Task HandleAsync_DestructiveCommand_ClassifierDenies()
    {
        var classifier = new StubCommandClassifier(
            new CommandClassification(CommandCategory.Destructive, [CommandRisk.FileDeletion], "rm detected"));
        var queueOps = new StubQueueOps();
        var ctx = new PermissionContext(
            "bash", MakeInput("rm -rf /"), "msg1", "tool1",
            new StubPermissionLogger(), queueOps);

        var handler = new CoordinatorHandler();
        var result = await handler.HandleAsync(new CoordinatorPermissionParams
        {
            Context = ctx,
            PendingClassifierCheck = new object(),
            Classifier = classifier,
            HookExecutor = new StubHookExecutor()
        }).ConfigureAwait(true);

        Assert.NotNull(result);
        Assert.Equal(PermissionBehavior.Deny, result.Behavior);
    }

    [Fact]
    public async Task HandleAsync_UnknownCommand_ClassifierReturnsNull()
    {
        var classifier = new StubCommandClassifier(
            new CommandClassification(CommandCategory.Unknown, []));
        var queueOps = new StubQueueOps();
        var ctx = new PermissionContext(
            "bash", MakeInput("npm run build"), "msg1", "tool1",
            new StubPermissionLogger(), queueOps);

        var handler = new CoordinatorHandler();
        var result = await handler.HandleAsync(new CoordinatorPermissionParams
        {
            Context = ctx,
            PendingClassifierCheck = new object(),
            Classifier = classifier,
            HookExecutor = new StubHookExecutor()
        }).ConfigureAwait(true);

        Assert.Null(result);
    }

    [Fact]
    public async Task HandleAsync_NoClassifier_ReturnsNull()
    {
        var queueOps = new StubQueueOps();
        var ctx = new PermissionContext(
            "bash", MakeInput("ls -la"), "msg1", "tool1",
            new StubPermissionLogger(), queueOps);

        var handler = new CoordinatorHandler();
        var result = await handler.HandleAsync(new CoordinatorPermissionParams
        {
            Context = ctx,
            PendingClassifierCheck = new object(),
            Classifier = null,
            HookExecutor = new StubHookExecutor()
        }).ConfigureAwait(true);

        Assert.Null(result);
    }

    [Fact]
    public async Task HandleAsync_NonBashTool_ClassifierSkipped()
    {
        var classifier = new StubCommandClassifier(
            new CommandClassification(CommandCategory.ReadOnly, []));
        var queueOps = new StubQueueOps();
        var ctx = new PermissionContext(
            "fileedit", MakeInput("some file"), "msg1", "tool1",
            new StubPermissionLogger(), queueOps);

        var handler = new CoordinatorHandler();
        var result = await handler.HandleAsync(new CoordinatorPermissionParams
        {
            Context = ctx,
            PendingClassifierCheck = new object(),
            Classifier = classifier,
            HookExecutor = new StubHookExecutor()
        }).ConfigureAwait(true);

        Assert.Null(result);
    }

    [Fact]
    public async Task HandleAsync_NoPendingCheck_ClassifierSkipped()
    {
        var classifier = new StubCommandClassifier(
            new CommandClassification(CommandCategory.ReadOnly, []));
        var queueOps = new StubQueueOps();
        var ctx = new PermissionContext(
            "bash", MakeInput("ls -la"), "msg1", "tool1",
            new StubPermissionLogger(), queueOps);

        var handler = new CoordinatorHandler();
        var result = await handler.HandleAsync(new CoordinatorPermissionParams
        {
            Context = ctx,
            PendingClassifierCheck = null,
            Classifier = classifier,
            HookExecutor = new StubHookExecutor()
        }).ConfigureAwait(true);

        Assert.Null(result);
    }

    [Fact]
    public async Task HandleAsync_PathViolation_ClassifierDenies()
    {
        var classifier = new StubCommandClassifier(
            new CommandClassification(CommandCategory.PathViolation, [CommandRisk.PathEscape], "path escape"));
        var queueOps = new StubQueueOps();
        var ctx = new PermissionContext(
            "bash", MakeInput("cat /etc/passwd"), "msg1", "tool1",
            new StubPermissionLogger(), queueOps);

        var handler = new CoordinatorHandler();
        var result = await handler.HandleAsync(new CoordinatorPermissionParams
        {
            Context = ctx,
            PendingClassifierCheck = new object(),
            Classifier = classifier,
            HookExecutor = new StubHookExecutor()
        }).ConfigureAwait(true);

        Assert.NotNull(result);
        Assert.Equal(PermissionBehavior.Deny, result.Behavior);
    }

    private sealed class StubHookExecutor : IPermissionHookExecutor
    {
        public Task RegisterHookAsync(IPermissionHook hook, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task UnregisterHookAsync(string hookName, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public async IAsyncEnumerable<PermissionHookResult> ExecuteHooksAsync(
            string toolName, string toolUseId, Dictionary<string, JsonElement> input,
            string? permissionMode, List<HookPermissionUpdate>? suggestions,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask.ConfigureAwait(true);
            yield break;
        }
    }
}
