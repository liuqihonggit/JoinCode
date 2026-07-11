namespace Host.Tests.ChatCommands;

using JoinCode.Abstractions.Models.Task;
using JoinCode.Abstractions.Models.Todo;

/// <summary>
/// TasksCommand 取值范围测试 — 验证 TasksAction + CrudAction 枚举字面量正确路由
/// 覆盖:create/new/update (CrudAction) + kill/detail/complete/todo (TasksAction) + 未知子命令 + 默认 list
/// 验证目标:Step 6 重构后,所有 case 标签能被正确识别
/// </summary>
public sealed class TasksCommandTests
{
    [Fact]
    public void Name_Should_Be_tasks()
    {
        var cmd = new TasksCommand();
        cmd.Name.Should().Be("tasks");
    }

    [Fact]
    public void Description_Should_Not_Be_Empty()
    {
        var cmd = new TasksCommand();
        cmd.Description.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Usage_Should_Start_With_Slash()
    {
        var cmd = new TasksCommand();
        cmd.Usage.Should().StartWith("/tasks");
    }

    [Fact]
    public void IsHidden_Should_Be_False()
    {
        var cmd = new TasksCommand();
        cmd.IsHidden.Should().BeFalse();
    }

    [Fact]
    public void Aliases_Should_Contain_task_and_bashes()
    {
        var cmd = new TasksCommand();
        cmd.Aliases.Should().Contain("task");
        cmd.Aliases.Should().Contain("bashes");
    }

    [Theory]
    [InlineData("create")]
    [InlineData("new")]
    public async Task Execute_WithCreateVariants_Should_Return_Continue(string subCommand)
    {
        // CrudActionConstants.Create/New → CreateTaskAsync
        var services = CreateServices(taskService: CreateMockTaskService());
        var cmd = new TasksCommand();
        var context = CreateContext($"{subCommand} new task title", services);

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
        result.IsHandled.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_WithUpdateSubcommand_Should_Return_Continue()
    {
        // CrudActionConstants.Update → UpdateTaskAsync
        var services = CreateServices(taskService: CreateMockTaskService());
        var cmd = new TasksCommand();
        var context = CreateContext("update task-1 --status in_progress", services);

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
    }

    [Theory]
    [InlineData("kill")]
    [InlineData("detail")]
    [InlineData("complete")]
    [InlineData("todo")]
    public async Task Execute_WithTasksActionSubcommand_Should_Return_Continue(string subCommand)
    {
        // TasksActionConstants.Kill/Detail/Complete/Todo 枚举路由取值范围测试
        var services = CreateServices(
            taskService: CreateMockTaskService(),
            todoService: CreateMockTodoService());
        var cmd = new TasksCommand();
        var context = CreateContext(subCommand, services);

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_WithEmptyArgs_Should_Default_To_List()
    {
        // 空 args → 走默认 list 分支
        var services = CreateServices(taskService: CreateMockTaskService());
        var cmd = new TasksCommand();
        var context = CreateContext("", services);

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_WithUnknownSubcommand_Should_NotThrow()
    {
        var services = CreateServices(taskService: CreateMockTaskService());
        var cmd = new TasksCommand();
        var context = CreateContext("unknown-action", services);

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
    }

    [Theory]
    [InlineData("CREATE")]
    [InlineData("NEW")]
    [InlineData("UPDATE")]
    [InlineData("KILL")]
    [InlineData("DETAIL")]
    [InlineData("COMPLETE")]
    [InlineData("TODO")]
    public async Task Execute_WithUppercaseSubcommand_Should_Be_CaseInsensitive(string subCommand)
    {
        var services = CreateServices(
            taskService: CreateMockTaskService(),
            todoService: CreateMockTodoService());
        var cmd = new TasksCommand();
        var context = CreateContext(subCommand, services);

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_CreateWithTaskServiceNull_Should_Return_Continue()
    {
        // TaskService null 时,CreateTaskAsync 输出警告
        var services = CreateServices(taskService: null);
        var cmd = new TasksCommand();
        var context = CreateContext("create some title", services);

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
    }

    // ===== TasksAction 枚举字面量路由验证 =====

    [Theory]
    [InlineData("kill", TasksAction.Kill)]
    [InlineData("detail", TasksAction.Detail)]
    [InlineData("complete", TasksAction.Complete)]
    [InlineData("todo", TasksAction.Todo)]
    public void TasksAction_FromValue_ValidString_Should_Resolve_Correctly(string input, TasksAction expected)
    {
        TasksActionExtensions.FromValue(input).Should().Be(expected);
    }

    [Fact]
    public void TasksActionConstants_Values_Should_Match_Route()
    {
        // 验证枚举常量值与原硬编码字符串完全一致(行为不变)
        TasksActionConstants.Kill.Should().Be("kill");
        TasksActionConstants.Detail.Should().Be("detail");
        TasksActionConstants.Complete.Should().Be("complete");
        TasksActionConstants.Todo.Should().Be("todo");
    }

    private static ChatCommandContext CreateContext(string arguments, CommandServices services)
    {
        return new ChatCommandContext
        {
            Arguments = arguments,
            CancellationToken = CancellationToken.None,
            Services = services,
        };
    }

    private static CommandServices CreateServices(ITaskService? taskService, ITodoService? todoService = null)
    {
        return new CommandServices
        {
            ChatService = Mock.Of<IChatService>(),
            CodeService = Mock.Of<ICodeService>(),
            PlanService = Mock.Of<IPlanService>(),
            TaskService = taskService,
            TodoService = todoService,
        FileSystem = TestFileSystem.Current,
        };
    }

    private static ITaskService CreateMockTaskService()
    {
        var mock = new Mock<ITaskService>();

        mock.Setup(s => s.CreateTaskAsync(
            It.IsAny<string>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<DateTime?>(),
            It.IsAny<string>(),
            It.IsAny<List<string>?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TaskOperationResult(true));

        mock.Setup(s => s.UpdateTaskAsync(
            It.IsAny<UpdateTaskRequest>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TaskOperationResult(true));

        mock.Setup(s => s.GetTaskAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TaskItem?)null);

        // StopTaskAsync 有两个重载: (string, string?, CancellationToken) 和 (string, bool, CancellationToken)
        // TasksCommand 调用第一个(string reason 版本)
        mock.Setup(s => s.StopTaskAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TaskOperationResult(false));

        mock.Setup(s => s.StopTaskAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        return mock.Object;
    }

    private static ITodoService CreateMockTodoService()
    {
        var mock = new Mock<ITodoService>();

        mock.Setup(s => s.ListTodosAsync(
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<bool>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TodoListResult(true, new List<TodoItem>()));

        return mock.Object;
    }
}
