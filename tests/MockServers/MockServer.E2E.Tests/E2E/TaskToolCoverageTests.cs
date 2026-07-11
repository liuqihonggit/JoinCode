namespace MockServer.E2E.Tests;

/// <summary>
/// 任务与调度工具 E2E 覆盖测试 — 拆分自 CoverageExpansionTests 以启用 xUnit 集合并行
/// 包含 TaskCreate/List/Get/Stop/Update/Output 及 CronCreate/Delete/List
/// </summary>
public sealed class TaskToolCoverageTests : CoverageTestBase
{
    public TaskToolCoverageTests(ITestOutputHelper output) : base(output) { }

    // ============================================================
    // 任务工具测试
    // ============================================================

    [Fact]
    public async Task TaskCreate_ShouldCreateNewTask()
    {
        await RunScriptAsync(TaskToolScripts.TaskCreateTest).ConfigureAwait(true);
    }

    [Fact]
    public async Task TaskList_ShouldListTasks()
    {
        await RunScriptAsync(ExtendedToolScripts.TaskListTest).ConfigureAwait(true);
    }

    [Fact]
    public async Task TaskGet_ShouldGetTaskDetail()
    {
        await RunScriptAsync(ExtendedToolScripts.TaskGetTest).ConfigureAwait(true);
    }

    [Fact]
    public async Task TaskStop_ShouldStopTask()
    {
        await RunScriptAsync(ExtendedToolScripts.TaskStopTest).ConfigureAwait(true);
    }

    [Fact]
    public async Task TaskUpdate_ShouldUpdateTask()
    {
        await RunScriptAsync(ExtendedToolScripts.TaskUpdateTest).ConfigureAwait(true);
    }

    [Fact]
    public async Task TaskOutput_ShouldGetTaskOutput()
    {
        await RunScriptAsync(ExtendedToolScripts.TaskOutputTest).ConfigureAwait(true);
    }

    // ============================================================
    // 调度工具测试
    // ============================================================

    [Fact]
    public async Task CronCreate_ShouldCreateCronJob()
    {
        await RunScriptAsync(ExtendedToolScripts.CronCreateTest).ConfigureAwait(true);
    }

    [Fact]
    public async Task CronDelete_ShouldDeleteCronJob()
    {
        await RunScriptAsync(ExtendedToolScripts.CronDeleteTest).ConfigureAwait(true);
    }

    [Fact]
    public async Task CronList_ShouldListScheduledTasks()
    {
        await RunScriptAsync(SchedulingToolScripts.CronListTest).ConfigureAwait(true);
    }
}
