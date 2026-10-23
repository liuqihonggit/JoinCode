namespace JoinCode.Gui.Tests.Hosting;

/// <summary>
/// JccChatSession.GetAvailableSkillsAsync 映射测试（TASK066 C6）：
/// 引擎 SkillDefinition → GUI SkillSummary（DisableModelInvocation 取反、SourcePath 透传）；
/// 未注册 ISkillService 时返回空列表，DSH skills/list 仍可渲染空集。
/// </summary>
public sealed class JccChatSessionSkillsTests {
    [Fact]
    public async Task GetAvailableSkills_MapsEngineDefinitions() {
        await using var skillService = new StubSkillService([
            new SkillDefinition {
                Name = "code-review",
                Description = "审查代码",
                Steps = [],
                DisableModelInvocation = true,
                SourcePath = "/skills/code-review.md",
            },
            new SkillDefinition {
                Name = "commit",
                Description = "提交变更",
                Steps = [],
            },
        ]);
        var provider = new ServiceCollection().AddSingleton<ISkillService>(skillService).BuildServiceProvider();
        #nullable disable
        await using var session = new JccChatSession(provider, null, new WorkflowConfig());
        #nullable enable

        var skills = await session.GetAvailableSkillsAsync(CancellationToken.None);

        skills.Should().HaveCount(2);
        skills[0].Name.Should().Be("code-review");
        skills[0].Description.Should().Be("审查代码");
        skills[0].ModelInvocable.Should().BeFalse("DisableModelInvocation=true 表示模型不可自动调用");
        skills[0].Path.Should().Be("/skills/code-review.md");
        skills[1].ModelInvocable.Should().BeTrue();
        skills[1].Path.Should().BeNull();
    }

    [Fact]
    public async Task GetAvailableSkills_WithoutSkillService_ReturnsEmpty() {
        #nullable disable
        await using var session = new JccChatSession(
            new ServiceCollection().BuildServiceProvider(),
            null,
            new WorkflowConfig());
        #nullable enable

        var skills = await session.GetAvailableSkillsAsync(CancellationToken.None);

        skills.Should().BeEmpty();
    }

    /// <summary>只实现技能清单查询的桩 — 其余技能执行能力测试未使用</summary>
    private sealed class StubSkillService(IReadOnlyList<SkillDefinition> skills) : ISkillService {
        public Task<IReadOnlyList<SkillDefinition>> GetAvailableSkillsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(skills);

        public Task<SkillResult> ExecuteAsync(
            string skillName,
            Dictionary<string, JsonElement>? parameters,
            JoinCode.Abstractions.Execution.ExecutionContext ctx,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SkillDefinition?> GetSkillAsync(string skillName, CancellationToken cancellationToken = default)
            => Task.FromResult<SkillDefinition?>(null);

        public bool SkillExists(string skillName) => false;

        public Task<bool> ReloadAsync(string? skillName, JoinCode.Abstractions.Execution.ExecutionContext ctx, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public void RegisterSkill(SkillDefinition skill) { }

        public bool UnregisterSkill(string skillName) => false;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
