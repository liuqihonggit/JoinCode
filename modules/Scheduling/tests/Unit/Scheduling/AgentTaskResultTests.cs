
namespace Core.Tests.Scheduling;

/// <summary>
/// AgentTaskResult 单元测试类
/// 测试任务结果的各种功能，包括结果创建和构建器模式
/// </summary>
public class AgentTaskResultTests
{
    #region 结果创建测试 - Success 方法

    /// <summary>
    /// 测试 Success 方法应创建成功的结果
    /// </summary>
    [Fact]
    public void Success_ShouldCreateSuccessfulResult()
    {
        var result = AgentTaskResult.Success(
            "task-001",
            "agent-001",
            "成功输出",
            1000);

        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        result.TaskId.Should().Be("task-001");
        result.AgentId.Should().Be("agent-001");
        result.Output.Should().Be("成功输出");
        result.ExecutionTimeMs.Should().Be(1000);
    }

    /// <summary>
    /// 测试 Success 方法应自动设置时间戳
    /// </summary>
    [Fact]
    public void Success_ShouldSetTimestamps()
    {
        var beforeCreation = DateTime.UtcNow.AddSeconds(-1);

        var result = AgentTaskResult.Success("task-001", "agent-001", "输出", 100);

        var afterCreation = DateTime.UtcNow.AddSeconds(1);

        result.StartedAt.Should().BeAfter(beforeCreation.AddMilliseconds(-100));
        result.CompletedAt.Should().BeBefore(afterCreation);
        result.CompletedAt.Should().BeAfter(result.StartedAt);
    }

    /// <summary>
    /// 测试 Success 方法创建的结果错误应为 null
    /// </summary>
    [Fact]
    public void Success_ShouldHaveNullError()
    {
        var result = AgentTaskResult.Success("task-001", "agent-001", "输出", 100);

        result.Error.Should().BeNull();
    }

    #endregion

    #region 结果创建测试 - Failure 方法

    /// <summary>
    /// 测试 Failure 方法应创建失败的结果
    /// </summary>
    [Fact]
    public void Failure_ShouldCreateFailedResult()
    {
        var result = AgentTaskResult.Failure(
            "task-001",
            "agent-001",
            "错误信息",
            500);

        result.Should().NotBeNull();
        result.IsSuccess.Should().BeFalse();
        result.TaskId.Should().Be("task-001");
        result.AgentId.Should().Be("agent-001");
        result.Error.Should().Be("错误信息");
        result.ExecutionTimeMs.Should().Be(500);
    }

    /// <summary>
    /// 测试 Failure 方法默认执行时间应为 0
    /// </summary>
    [Fact]
    public void Failure_DefaultExecutionTime_ShouldBeZero()
    {
        var result = AgentTaskResult.Failure("task-001", "agent-001", "错误");

        result.ExecutionTimeMs.Should().Be(0);
    }

    /// <summary>
    /// 测试 Failure 方法创建的结果输出应为空字符串
    /// </summary>
    [Fact]
    public void Failure_ShouldHaveEmptyOutput()
    {
        var result = AgentTaskResult.Failure("task-001", "agent-001", "错误");

        result.Output.Should().BeEmpty();
    }

    /// <summary>
    /// 测试 Failure 方法应自动设置时间戳
    /// </summary>
    [Fact]
    public void Failure_ShouldSetTimestamps()
    {
        var beforeCreation = DateTime.UtcNow.AddSeconds(-1);

        var result = AgentTaskResult.Failure("task-001", "agent-001", "错误", 100);

        var afterCreation = DateTime.UtcNow.AddSeconds(1);

        result.StartedAt.Should().BeAfter(beforeCreation.AddMilliseconds(-100));
        result.CompletedAt.Should().BeBefore(afterCreation);
    }

    #endregion

    #region 元数据操作测试

    /// <summary>
    /// 测试 WithMetadata 应添加元数据并支持链式调用
    /// </summary>
    [Fact]
    public void WithMetadata_ShouldAddMetadataAndSupportChaining()
    {
        var result = AgentTaskResult.Success("task-001", "agent-001", "输出", 100)
            .WithMetadata("key1", "value1")
            .WithMetadata("key2", 42);

        result.GetMetadataValue<string>("key1").Should().Be("value1");
        result.GetMetadataValue<int>("key2").Should().Be(42);
    }

    /// <summary>
    /// 测试 WithMetadata 批量添加元数据
    /// </summary>
    [Fact]
    public void WithMetadata_Batch_ShouldAddMultipleMetadata()
    {
        var metadata = new Dictionary<string, JsonElement>
        {
            { "key1", JsonSerializer.SerializeToElement("value1", SchedulingJsonContext.Default.String) },
            { "key2", JsonSerializer.SerializeToElement(42, SchedulingJsonContext.Default.Int32) },
            { "key3", JsonSerializer.SerializeToElement(true, SchedulingJsonContext.Default.Boolean) }
        };

        var result = AgentTaskResult.Success("task-001", "agent-001", "输出", 100)
            .WithMetadata(metadata);

        result.GetMetadataValue<string>("key1").Should().Be("value1");
        result.GetMetadataValue<int>("key2").Should().Be(42);
        result.GetMetadataValue<bool>("key3").Should().BeTrue();
    }

    /// <summary>
    /// 测试 GetMetadataValue 返回默认值当键不存在时
    /// </summary>
    [Fact]
    public void GetMetadataValue_NonExistentKey_ShouldReturnDefault()
    {
        var result = AgentTaskResult.Success("task-001", "agent-001", "输出", 100);

        var value = result.GetMetadataValue<string>("non-existent");

        value.Should().BeNull();
    }

    /// <summary>
    /// 测试 GetMetadataValue 使用提供的默认值
    /// </summary>
    [Fact]
    public void GetMetadataValue_WithDefaultValue_ShouldReturnProvidedDefault()
    {
        var result = AgentTaskResult.Success("task-001", "agent-001", "输出", 100);

        var value = result.GetMetadataValue("non-existent", "default");

        value.Should().Be("default");
    }

    /// <summary>
    /// 测试 Metadata 属性返回元数据字典副本
    /// </summary>
    [Fact]
    public void MetadataProperty_ShouldReturnCopyOfMetadata()
    {
        var result = AgentTaskResult.Success("task-001", "agent-001", "输出", 100)
            .WithMetadata("key", "value");

        var metadata1 = result.GetMetadata();
        var metadata2 = result.GetMetadata();

        metadata1.Should().NotBeSameAs(metadata2);
        metadata1.Should().BeEquivalentTo(metadata2);
    }

    /// <summary>
    /// 测试修改返回的元数据字典不应影响原始结果
    /// </summary>
    [Fact]
    public void MetadataProperty_ModifyingCopy_ShouldNotAffectOriginal()
    {
        var result = AgentTaskResult.Success("task-001", "agent-001", "输出", 100)
            .WithMetadata("key", "value");

        var metadata = result.GetMetadata();
        metadata["key"] = JsonSerializer.SerializeToElement("modified", SchedulingJsonContext.Default.String);

        result.GetMetadataValue<string>("key").Should().Be("value");
    }

    #endregion

    #region WithAgentInfo 测试

    /// <summary>
    /// 测试 WithAgentInfo 应创建新的结果实例并设置 Agent 信息
    /// </summary>
    [Fact]
    public void WithAgentInfo_ShouldCreateNewInstanceWithAgentInfo()
    {
        var original = AgentTaskResult.Success("task-001", "agent-001", "输出", 100);
        var modified = original.WithAgentInfo("测试Agent", 2);

        modified.Should().NotBeSameAs(original);
        modified.AgentName.Should().Be("测试Agent");
        modified.AgentIndex.Should().Be(2);
    }

    /// <summary>
    /// 测试 WithAgentInfo 应保留原始结果的其他属性
    /// </summary>
    [Fact]
    public void WithAgentInfo_ShouldPreserveOtherProperties()
    {
        var original = AgentTaskResult.Success("task-001", "agent-001", "输出", 100)
            .WithMetadata("key", "value");
        var modified = original.WithAgentInfo("测试Agent", 2);

        modified.TaskId.Should().Be(original.TaskId);
        modified.AgentId.Should().Be(original.AgentId);
        modified.IsSuccess.Should().Be(original.IsSuccess);
        modified.Output.Should().Be(original.Output);
        modified.ExecutionTimeMs.Should().Be(original.ExecutionTimeMs);
        modified.GetMetadataValue<string>("key").Should().Be("value");
    }

    #endregion

    #region ToString 测试

    /// <summary>
    /// 测试成功结果的 ToString 应包含成功标识
    /// </summary>
    [Fact]
    public void ToString_SuccessResult_ShouldContainSuccessIndicator()
    {
        var result = AgentTaskResult.Success("task-001", "agent-001", "输出", 100);

        var str = result.ToString();

        str.Should().Contain("成功");
        str.Should().Contain("task-001");
        str.Should().Contain("agent-001");
        str.Should().Contain("100ms");
    }

    /// <summary>
    /// 测试失败结果的 ToString 应包含失败标识
    /// </summary>
    [Fact]
    public void ToString_FailureResult_ShouldContainFailureIndicator()
    {
        var result = AgentTaskResult.Failure("task-001", "agent-001", "错误", 50);

        var str = result.ToString();

        str.Should().Contain("失败");
        str.Should().Contain("task-001");
        str.Should().Contain("agent-001");
    }

    #endregion

    #region IAgentTaskResult 接口实现测试

    /// <summary>
    /// 测试 AgentTaskResult 实现 IAgentTaskResult 接口
    /// </summary>
    [Fact]
    public void AgentTaskResult_ShouldImplementIAgentTaskResult()
    {
        var result = AgentTaskResult.Success("task-001", "agent-001", "输出", 100);

        result.Should().BeAssignableTo<IAgentTaskResult>();
    }

    /// <summary>
    /// 测试通过接口访问结果属性
    /// </summary>
    [Fact]
    public void IAgentTaskResult_ShouldAllowPropertyAccess()
    {
        IAgentTaskResult result = AgentTaskResult.Success("task-001", "agent-001", "输出内容", 1000);

        result.TaskId.Should().Be("task-001");
        result.IsSuccess.Should().BeTrue();
        result.Output.Should().Be("输出内容");
        result.Error.Should().BeNull();
        result.ExecutionTimeMs.Should().Be(1000);
    }

    /// <summary>
    /// 测试通过接口操作元数据
    /// </summary>
    [Fact]
    public void IAgentTaskResult_ShouldAllowMetadataAccess()
    {
        var result = AgentTaskResult.Success("task-001", "agent-001", "输出", 100)
            .WithMetadata("key", "value");

        IAgentTaskResult interfaceResult = result;

        interfaceResult.GetMetadata().Should().ContainKey("key");
        interfaceResult.GetMetadataValue<string>("key").Should().Be("value");
    }

    #endregion

    #region 边界条件测试

    /// <summary>
    /// 测试空输出不应导致问题
    /// </summary>
    [Fact]
    public void Success_WithEmptyOutput_ShouldWork()
    {
        var result = AgentTaskResult.Success("task-001", "agent-001", "", 100);

        result.Output.Should().BeEmpty();
    }

    /// <summary>
    /// 测试长输出应被正确处理
    /// </summary>
    [Fact]
    public void Success_WithLongOutput_ShouldWork()
    {
        var longOutput = new string('x', 10000);
        var result = AgentTaskResult.Success("task-001", "agent-001", longOutput, 100);

        result.Output.Should().Be(longOutput);
        result.Output.Length.Should().Be(10000);
    }

    /// <summary>
    /// 测试零执行时间应被正确处理
    /// </summary>
    [Fact]
    public void Success_WithZeroExecutionTime_ShouldWork()
    {
        var result = AgentTaskResult.Success("task-001", "agent-001", "输出", 0);

        result.ExecutionTimeMs.Should().Be(0);
    }

    /// <summary>
    /// 测试负执行时间（虽然不合理但应被接受）
    /// </summary>
    [Fact]
    public void Success_WithNegativeExecutionTime_ShouldAccept()
    {
        var result = AgentTaskResult.Success("task-001", "agent-001", "输出", -100);

        result.ExecutionTimeMs.Should().Be(-100);
    }

    /// <summary>
    /// 测试错误信息为空字符串
    /// </summary>
    [Fact]
    public void Failure_WithEmptyError_ShouldWork()
    {
        var result = AgentTaskResult.Failure("task-001", "agent-001", "");

        result.Error.Should().BeEmpty();
    }

    /// <summary>
    /// 测试错误信息为长字符串
    /// </summary>
    [Fact]
    public void Failure_WithLongError_ShouldWork()
    {
        var longError = new string('e', 5000);
        var result = AgentTaskResult.Failure("task-001", "agent-001", longError);

        result.Error.Should().Be(longError);
    }

    #endregion
}
