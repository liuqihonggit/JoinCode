
namespace Core.Tests.Scheduling;

/// <summary>
/// AgentTaskContext 单元测试类
/// 测试任务上下文的各种功能，包括上下文创建、元数据操作和取消令牌
/// </summary>
public class AgentTaskContextTests
{
    #region 上下文创建测试

    /// <summary>
    /// 测试使用必需属性创建上下文
    /// </summary>
    [Fact]
    public void CreateContext_WithRequiredProperties_ShouldCreateSuccessfully()
    {
        var context = new AgentTaskContext
        {
            TaskId = "task-001",
            AgentIndex = 0,
            TotalAgents = 3,
            WorkScope = "测试工作范围",
            TaskName = "测试任务",
            Description = "这是一个测试任务",
            CancellationToken = CancellationToken.None
        };

        context.Should().NotBeNull();
        context.TaskId.Should().Be("task-001");
        context.AgentIndex.Should().Be(0);
        context.TotalAgents.Should().Be(3);
        context.WorkScope.Should().Be("测试工作范围");
        context.TaskName.Should().Be("测试任务");
        context.Description.Should().Be("这是一个测试任务");
    }

    /// <summary>
    /// 测试上下文创建时自动生成 CreatedAt
    /// </summary>
    [Fact]
    public void CreateContext_ShouldAutoGenerateCreatedAt()
    {
        var beforeCreation = DateTime.UtcNow.AddSeconds(-1);

        var context = new AgentTaskContext
        {
            TaskId = "task-001",
            AgentIndex = 0,
            TotalAgents = 1,
            WorkScope = "范围",
            TaskName = "任务",
            Description = "描述",
            CancellationToken = CancellationToken.None
        };

        var afterCreation = DateTime.UtcNow.AddSeconds(1);

        context.CreatedAt.Should().BeAfter(beforeCreation);
        context.CreatedAt.Should().BeBefore(afterCreation);
    }

    /// <summary>
    /// 测试上下文默认优先级为 0
    /// </summary>
    [Fact]
    public void CreateContext_DefaultPriority_ShouldBeZero()
    {
        var context = new AgentTaskContext
        {
            TaskId = "task-001",
            AgentIndex = 0,
            TotalAgents = 1,
            WorkScope = "范围",
            TaskName = "任务",
            Description = "描述",
            CancellationToken = CancellationToken.None
        };

        context.Priority.Should().Be(0);
    }

    /// <summary>
    /// 测试上下文默认 ParentTaskId 为 null
    /// </summary>
    [Fact]
    public void CreateContext_DefaultParentTaskId_ShouldBeNull()
    {
        var context = new AgentTaskContext
        {
            TaskId = "task-001",
            AgentIndex = 0,
            TotalAgents = 1,
            WorkScope = "范围",
            TaskName = "任务",
            Description = "描述",
            CancellationToken = CancellationToken.None
        };

        context.ParentTaskId.Should().BeNull();
    }

    /// <summary>
    /// 测试设置自定义优先级
    /// </summary>
    [Fact]
    public void CreateContext_WithCustomPriority_ShouldSetCorrectly()
    {
        var context = new AgentTaskContext
        {
            TaskId = "task-001",
            AgentIndex = 0,
            TotalAgents = 1,
            WorkScope = "范围",
            TaskName = "任务",
            Description = "描述",
            Priority = 5,
            CancellationToken = CancellationToken.None
        };

        context.Priority.Should().Be(5);
    }

    /// <summary>
    /// 测试设置父任务 ID
    /// </summary>
    [Fact]
    public void CreateContext_WithParentTaskId_ShouldSetCorrectly()
    {
        var context = new AgentTaskContext
        {
            TaskId = "task-001",
            AgentIndex = 0,
            TotalAgents = 1,
            WorkScope = "范围",
            TaskName = "任务",
            Description = "描述",
            ParentTaskId = "parent-task-001",
            CancellationToken = CancellationToken.None
        };

        context.ParentTaskId.Should().Be("parent-task-001");
    }

    #endregion

    #region 子上下文创建测试

    /// <summary>
    /// 测试 CreateSubContext 应创建有效的子上下文
    /// </summary>
    [Fact]
    public void CreateSubContext_ShouldCreateValidSubContext()
    {
        var parentContext = new AgentTaskContext
        {
            TaskId = "parent-001",
            AgentIndex = 0,
            TotalAgents = 2,
            WorkScope = "父工作范围",
            TaskName = "父任务",
            Description = "父任务描述",
            Priority = 3,
            CancellationToken = CancellationToken.None
        };

        var subContext = parentContext.CreateSubContext(
            "sub-001",
            "子任务",
            "子任务描述",
            "子工作范围");

        subContext.Should().NotBeNull();
        subContext.TaskId.Should().Be("sub-001");
        subContext.TaskName.Should().Be("子任务");
        subContext.Description.Should().Be("子任务描述");
        subContext.WorkScope.Should().Be("子工作范围");
        subContext.ParentTaskId.Should().Be("parent-001");
        subContext.Priority.Should().Be(3);
    }

    /// <summary>
    /// 测试子上下文应继承父上下文的取消令牌
    /// </summary>
    [Fact]
    public void CreateSubContext_ShouldInheritCancellationToken()
    {
        using var cts = new CancellationTokenSource();
        var parentContext = new AgentTaskContext
        {
            TaskId = "parent-001",
            AgentIndex = 0,
            TotalAgents = 1,
            WorkScope = "范围",
            TaskName = "任务",
            Description = "描述",
            CancellationToken = cts.Token,
            CancellationTokenSource = cts
        };

        var subContext = parentContext.CreateSubContext(
            "sub-001",
            "子任务",
            "子任务描述",
            "子工作范围");

        subContext.CancellationToken.Should().Be(cts.Token);
        subContext.CancellationTokenSource.Should().Be(cts);
    }

    /// <summary>
    /// 测试子上下文应重置 AgentIndex 和 TotalAgents
    /// </summary>
    [Fact]
    public void CreateSubContext_ShouldResetAgentIndexAndTotalAgents()
    {
        var parentContext = new AgentTaskContext
        {
            TaskId = "parent-001",
            AgentIndex = 2,
            TotalAgents = 5,
            WorkScope = "范围",
            TaskName = "任务",
            Description = "描述",
            CancellationToken = CancellationToken.None
        };

        var subContext = parentContext.CreateSubContext(
            "sub-001",
            "子任务",
            "子任务描述",
            "子工作范围");

        subContext.AgentIndex.Should().Be(0);
        subContext.TotalAgents.Should().Be(1);
    }

    #endregion

    #region 元数据操作测试

    /// <summary>
    /// 测试 SetMetadataValue 和 GetMetadataValue 应正确工作
    /// </summary>
    [Fact]
    public void Metadata_SetAndGetValue_ShouldWorkCorrectly()
    {
        var context = new AgentTaskContext
        {
            TaskId = "task-001",
            AgentIndex = 0,
            TotalAgents = 1,
            WorkScope = "范围",
            TaskName = "任务",
            Description = "描述",
            CancellationToken = CancellationToken.None
        };

        context.SetMetadataValue("key1", "value1");
        var value = context.GetMetadataValue<string>("key1");

        value.Should().Be("value1");
    }

    /// <summary>
    /// 测试 GetMetadataValue 返回默认值当键不存在时
    /// </summary>
    [Fact]
    public void GetMetadataValue_NonExistentKey_ShouldReturnDefault()
    {
        var context = new AgentTaskContext
        {
            TaskId = "task-001",
            AgentIndex = 0,
            TotalAgents = 1,
            WorkScope = "范围",
            TaskName = "任务",
            Description = "描述",
            CancellationToken = CancellationToken.None
        };

        var value = context.GetMetadataValue<string>("non-existent");

        value.Should().BeNull();
    }

    /// <summary>
    /// 测试 GetMetadataValue 使用提供的默认值
    /// </summary>
    [Fact]
    public void GetMetadataValue_WithDefaultValue_ShouldReturnProvidedDefault()
    {
        var context = new AgentTaskContext
        {
            TaskId = "task-001",
            AgentIndex = 0,
            TotalAgents = 1,
            WorkScope = "范围",
            TaskName = "任务",
            Description = "描述",
            CancellationToken = CancellationToken.None
        };

        var value = context.GetMetadataValue("non-existent", "default");

        value.Should().Be("default");
    }

    /// <summary>
    /// 测试 SetMetadataValue 更新现有值
    /// </summary>
    [Fact]
    public void SetMetadataValue_UpdateExisting_ShouldUpdateValue()
    {
        var context = new AgentTaskContext
        {
            TaskId = "task-001",
            AgentIndex = 0,
            TotalAgents = 1,
            WorkScope = "范围",
            TaskName = "任务",
            Description = "描述",
            CancellationToken = CancellationToken.None
        };

        context.SetMetadataValue("key", "old-value");
        context.SetMetadataValue("key", "new-value");

        context.GetMetadataValue<string>("key").Should().Be("new-value");
    }

    /// <summary>
    /// 测试 Metadata 属性返回元数据字典副本
    /// </summary>
    [Fact]
    public void MetadataProperty_ShouldReturnCopyOfMetadata()
    {
        var context = new AgentTaskContext
        {
            TaskId = "task-001",
            AgentIndex = 0,
            TotalAgents = 1,
            WorkScope = "范围",
            TaskName = "任务",
            Description = "描述",
            CancellationToken = CancellationToken.None
        };

        context.SetMetadataValue("key", "value");
        var metadata1 = context.GetMetadata();
        var metadata2 = context.GetMetadata();

        metadata1.Should().NotBeSameAs(metadata2);
        metadata1.Should().BeEquivalentTo(metadata2);
    }

    /// <summary>
    /// 测试不同类型元数据值
    /// </summary>
    [Theory]
    [InlineData("string-key", "string-value")]
    [InlineData("int-key", 42)]
    [InlineData("bool-key", true)]
    [InlineData("double-key", 3.14)]
    public void Metadata_DifferentTypes_ShouldWorkCorrectly(string key, object value)
    {
        var context = new AgentTaskContext
        {
            TaskId = "task-001",
            AgentIndex = 0,
            TotalAgents = 1,
            WorkScope = "范围",
            TaskName = "任务",
            Description = "描述",
            CancellationToken = CancellationToken.None
        };

        context.SetMetadataValue(key, value);
        var retrieved = context.GetMetadataValue<object>(key);

        retrieved.Should().Be(value);
    }

    /// <summary>
    /// 测试子上下文继承父上下文元数据
    /// </summary>
    [Fact]
    public void CreateSubContext_ShouldInheritParentMetadata()
    {
        var parentContext = new AgentTaskContext
        {
            TaskId = "parent-001",
            AgentIndex = 0,
            TotalAgents = 1,
            WorkScope = "范围",
            TaskName = "任务",
            Description = "描述",
            CancellationToken = CancellationToken.None
        };

        parentContext.SetMetadataValue("inherited-key", "inherited-value");

        var subContext = parentContext.CreateSubContext(
            "sub-001",
            "子任务",
            "子任务描述",
            "子工作范围");

        subContext.GetMetadataValue<string>("inherited-key").Should().Be("inherited-value");
    }

    #endregion

    #region 取消令牌测试

    /// <summary>
    /// 测试 IsCancellationRequested 当未取消时应返回 false
    /// </summary>
    [Fact]
    public void IsCancellationRequested_WhenNotCancelled_ShouldReturnFalse()
    {
        using var cts = new CancellationTokenSource();
        var context = new AgentTaskContext
        {
            TaskId = "task-001",
            AgentIndex = 0,
            TotalAgents = 1,
            WorkScope = "范围",
            TaskName = "任务",
            Description = "描述",
            CancellationToken = cts.Token,
            CancellationTokenSource = cts
        };

        context.IsCancellationRequested.Should().BeFalse();
    }

    /// <summary>
    /// 测试 IsCancellationRequested 当取消时应返回 true
    /// </summary>
    [Fact]
    public void IsCancellationRequested_WhenCancelled_ShouldReturnTrue()
    {
        using var cts = new CancellationTokenSource();
        var context = new AgentTaskContext
        {
            TaskId = "task-001",
            AgentIndex = 0,
            TotalAgents = 1,
            WorkScope = "范围",
            TaskName = "任务",
            Description = "描述",
            CancellationToken = cts.Token,
            CancellationTokenSource = cts
        };

        cts.Cancel();

        context.IsCancellationRequested.Should().BeTrue();
    }

    /// <summary>
    /// 测试 ThrowIfCancellationRequested 当未取消时不应抛出异常
    /// </summary>
    [Fact]
    public void ThrowIfCancellationRequested_WhenNotCancelled_ShouldNotThrow()
    {
        using var cts = new CancellationTokenSource();
        var context = new AgentTaskContext
        {
            TaskId = "task-001",
            AgentIndex = 0,
            TotalAgents = 1,
            WorkScope = "范围",
            TaskName = "任务",
            Description = "描述",
            CancellationToken = cts.Token,
            CancellationTokenSource = cts
        };

        var act = () => context.ThrowIfCancellationRequested();

        act.Should().NotThrow();
    }

    /// <summary>
    /// 测试 ThrowIfCancellationRequested 当取消时应抛出 OperationCanceledException
    /// </summary>
    [Fact]
    public void ThrowIfCancellationRequested_WhenCancelled_ShouldThrow()
    {
        using var cts = new CancellationTokenSource();
        var context = new AgentTaskContext
        {
            TaskId = "task-001",
            AgentIndex = 0,
            TotalAgents = 1,
            WorkScope = "范围",
            TaskName = "任务",
            Description = "描述",
            CancellationToken = cts.Token,
            CancellationTokenSource = cts
        };

        cts.Cancel();

        var act = () => context.ThrowIfCancellationRequested();

        act.Should().Throw<OperationCanceledException>();
    }

    /// <summary>
    /// 测试 Cancel 方法应取消任务
    /// </summary>
    [Fact]
    public void Cancel_ShouldCancelTask()
    {
        using var cts = new CancellationTokenSource();
        var context = new AgentTaskContext
        {
            TaskId = "task-001",
            AgentIndex = 0,
            TotalAgents = 1,
            WorkScope = "范围",
            TaskName = "任务",
            Description = "描述",
            CancellationToken = cts.Token,
            CancellationTokenSource = cts
        };

        context.Cancel();

        context.IsCancellationRequested.Should().BeTrue();
    }

    /// <summary>
    /// 测试 CancelAfter 方法应在指定时间后取消任务
    /// </summary>
    [Fact]
    public void CancelAfter_ShouldCancelAfterDelay()
    {
        var fakeTime = new FakeTimeProvider();
        using var cts = new CancellationTokenSource(TimeSpan.FromHours(1), fakeTime);
        var context = new AgentTaskContext
        {
            TaskId = "task-001",
            AgentIndex = 0,
            TotalAgents = 1,
            WorkScope = "范围",
            TaskName = "任务",
            Description = "描述",
            CancellationToken = cts.Token,
            CancellationTokenSource = cts
        };

        context.IsCancellationRequested.Should().BeFalse();

        context.CancelAfter(TimeSpan.FromMilliseconds(200));
        fakeTime.Advance(TimeSpan.FromMilliseconds(300));

        context.IsCancellationRequested.Should().BeTrue();
    }

    /// <summary>
    /// 测试 CreateLinkedToken 应创建链接令牌
    /// </summary>
    [Fact]
    public void CreateLinkedToken_ShouldCreateLinkedToken()
    {
        using var cts1 = new CancellationTokenSource();
        using var cts2 = new CancellationTokenSource();
        var context = new AgentTaskContext
        {
            TaskId = "task-001",
            AgentIndex = 0,
            TotalAgents = 1,
            WorkScope = "范围",
            TaskName = "任务",
            Description = "描述",
            CancellationToken = cts1.Token,
            CancellationTokenSource = cts1
        };

        var linkedToken = context.CreateLinkedToken(cts2.Token);

        linkedToken.Should().NotBeNull();
        linkedToken.IsCancellationRequested.Should().BeFalse();
    }

    /// <summary>
    /// 测试链接令牌在任一源取消时都应被取消
    /// </summary>
    [Fact]
    public void CreateLinkedToken_WhenEitherSourceCancels_ShouldCancelLinkedToken()
    {
        using var cts1 = new CancellationTokenSource();
        using var cts2 = new CancellationTokenSource();
        var context = new AgentTaskContext
        {
            TaskId = "task-001",
            AgentIndex = 0,
            TotalAgents = 1,
            WorkScope = "范围",
            TaskName = "任务",
            Description = "描述",
            CancellationToken = cts1.Token,
            CancellationTokenSource = cts1
        };

        var linkedToken = context.CreateLinkedToken(cts2.Token);

        cts2.Cancel();

        linkedToken.IsCancellationRequested.Should().BeTrue();
    }

    /// <summary>
    /// 测试当 CancellationTokenSource 为 null 时 CreateLinkedToken 应返回额外令牌
    /// </summary>
    [Fact]
    public void CreateLinkedToken_WithNullSource_ShouldReturnAdditionalToken()
    {
        using var cts = new CancellationTokenSource();
        var context = new AgentTaskContext
        {
            TaskId = "task-001",
            AgentIndex = 0,
            TotalAgents = 1,
            WorkScope = "范围",
            TaskName = "任务",
            Description = "描述",
            CancellationToken = CancellationToken.None,
            CancellationTokenSource = null
        };

        var linkedToken = context.CreateLinkedToken(cts.Token);

        linkedToken.Should().Be(cts.Token);
    }

    #endregion

    #region IAgentTaskContext 接口实现测试

    /// <summary>
    /// 测试 AgentTaskContext 实现 IAgentTaskContext 接口
    /// </summary>
    [Fact]
    public void AgentTaskContext_ShouldImplementIAgentTaskContext()
    {
        var context = new AgentTaskContext
        {
            TaskId = "task-001",
            AgentIndex = 0,
            TotalAgents = 1,
            WorkScope = "范围",
            TaskName = "任务",
            Description = "描述",
            CancellationToken = CancellationToken.None
        };

        context.Should().BeAssignableTo<IAgentTaskContext>();
    }

    /// <summary>
    /// 测试通过接口访问上下文属性
    /// </summary>
    [Fact]
    public void IAgentTaskContext_ShouldAllowPropertyAccess()
    {
        IAgentTaskContext context = new AgentTaskContext
        {
            TaskId = "task-001",
            AgentIndex = 1,
            TotalAgents = 3,
            WorkScope = "工作范围",
            TaskName = "任务名称",
            Description = "任务描述",
            Priority = 5,
            ParentTaskId = "parent-001",
            CancellationToken = CancellationToken.None
        };

        context.TaskId.Should().Be("task-001");
        context.AgentIndex.Should().Be(1);
        context.TotalAgents.Should().Be(3);
        context.WorkScope.Should().Be("工作范围");
        context.TaskName.Should().Be("任务名称");
        context.Description.Should().Be("任务描述");
        context.Priority.Should().Be(5);
        context.ParentTaskId.Should().Be("parent-001");
    }

    /// <summary>
    /// 测试通过接口操作元数据
    /// </summary>
    [Fact]
    public void IAgentTaskContext_ShouldAllowMetadataOperations()
    {
        IAgentTaskContext context = new AgentTaskContext
        {
            TaskId = "task-001",
            AgentIndex = 0,
            TotalAgents = 1,
            WorkScope = "范围",
            TaskName = "任务",
            Description = "描述",
            CancellationToken = CancellationToken.None
        };

        context.SetMetadataValue("key", "value");
        var value = context.GetMetadataValue<string>("key");

        value.Should().Be("value");
    }

    #endregion
}
