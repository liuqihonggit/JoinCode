namespace JoinCode.Plugins.Tests.Unit;

/// <summary>
/// PluginManager 单元测试 — 验证拆分出的 internal 纯计算子方法
/// <para>确定性测试:不依赖时序/IO/Actor mailbox,直接调用 static 纯函数</para>
/// <para>TestHooks 专项测试见 <see cref="PluginManagerTestHooksTest"/></para>
/// </summary>
public sealed class PluginManagerTest {
    /// <summary>
    /// OrderUnloadSequence 纯计算:三类插件名 → 统一卸载顺序 (external → native → workflow)
    /// </summary>
    public sealed class OrderUnloadSequenceTest {
        [Fact]
        public void AllEmpty_ReturnsEmpty() {
            PluginManager.OrderUnloadSequence([], [], []).Should().BeEmpty();
        }

        [Fact]
        public void OnlyExternal_ReturnsExternalKind() {
            var seq = PluginManager.OrderUnloadSequence(["e1", "e2"], [], []).ToList();

            seq.Should().HaveCount(2);
            seq.Should().AllSatisfy(t => t.Kind.Should().Be(PluginKind.External));
            seq.Select(t => t.Name).Should().Equal(["e1", "e2"]);
        }

        [Fact]
        public void OnlyNative_ReturnsNativeKind() {
            var seq = PluginManager.OrderUnloadSequence([], ["n1"], []).ToList();

            seq.Should().HaveCount(1);
            seq[0].Should().Be(("n1", PluginKind.Native));
        }

        [Fact]
        public void OnlyWorkflow_ReturnsWorkflowKind() {
            var seq = PluginManager.OrderUnloadSequence([], [], ["w1", "w2"]).ToList();

            seq.Should().HaveCount(2);
            seq.Should().AllSatisfy(t => t.Kind.Should().Be(PluginKind.Workflow));
        }

        [Fact]
        public void Mixed_ReturnsExternalThenNativeThenWorkflow() {
            var seq = PluginManager.OrderUnloadSequence(["e1"], ["n1"], ["w1"]).ToList();

            seq.Select(t => t.Kind)
                .Should().Equal([PluginKind.External, PluginKind.Native, PluginKind.Workflow]);
            seq.Select(t => t.Name).Should().Equal(["e1", "n1", "w1"]);
        }

        [Fact]
        public void Mixed_FullOrderIsExternalBeforeNativeBeforeWorkflow() {
            var seq = PluginManager.OrderUnloadSequence(["e1", "e2"], ["n1", "n2"], ["w1", "w2"]).ToList();

            seq.Select(t => t.Name)
                .Should().Equal(["e1", "e2", "n1", "n2", "w1", "w2"]);
        }

        [Fact]
        public void WorkflowOrderPreserved_AsGivenReversedOrder() {
            // 调用方须已按加载顺序逆序传入 workflowReversed,本函数不二次逆序
            var seq = PluginManager.OrderUnloadSequence([], [], ["w3", "w2", "w1"]).ToList();

            seq.Select(t => t.Name).Should().Equal(["w3", "w2", "w1"]);
        }

        [Fact]
        public void ExternalOrderPreserved() {
            var seq = PluginManager.OrderUnloadSequence(["eA", "eB", "eC"], [], []).ToList();

            seq.Select(t => t.Name).Should().Equal(["eA", "eB", "eC"]);
        }

        [Fact]
        public void NativeOrderPreserved() {
            var seq = PluginManager.OrderUnloadSequence([], ["nA", "nB"], []).ToList();

            seq.Select(t => t.Name).Should().Equal(["nA", "nB"]);
        }

        [Fact]
        public void LazyEvaluation_YieldsOneItemAtATime() {
            // 验证 yield 纯计算:多次枚举应得到相同结果(无共享可变状态)
            var seq = PluginManager.OrderUnloadSequence(["e1"], ["n1"], ["w1"]);

            var first = seq.ToList();
            var second = seq.ToList();

            first.Should().Equal(second);
        }

        [Fact]
        public void DuplicateNamesAcrossKinds_PreservedAsGiven() {
            // 不同类插件同名(理论不应发生,但纯函数不应去重)
            var seq = PluginManager.OrderUnloadSequence(["dup"], ["dup"], ["dup"]).ToList();

            seq.Should().HaveCount(3);
            seq.Select(t => t.Name).Should().Equal(["dup", "dup", "dup"]);
        }
    }

    /// <summary>
    /// LoadWorkflowPluginCoreAsync 拆分出的同步检查子方法确定性测试
    /// <para>不依赖 Actor mailbox/IO,直接调用 internal 方法验证状态检查行为</para>
    /// </summary>
    public sealed class LoadWorkflowChecksTest {
        private static PluginManager CreateManager() => new(new InMemoryFileSystem());

        [Fact]
        public async Task CheckNotDuplicateLoad_EmptyRegistry_DoesNotThrow() {
            var manager = CreateManager();
            await using var _ = manager;

            Action act = () => manager.CheckNotDuplicateLoad("any-plugin");

            act.Should().NotThrow();
        }

        [Fact]
        public async Task CheckNotBlacklisted_NotInBlacklist_DoesNotThrow() {
            var manager = CreateManager();
            await using var _ = manager;

            Action act = () => manager.CheckNotBlacklisted("any-plugin");

            act.Should().NotThrow();
        }

        [Fact]
        public async Task CheckNotBlacklisted_InBlacklist_ThrowsInvalidOperationExceptionWithPluginName() {
            var manager = CreateManager();
            await using var _ = manager;
            manager.AddToBlacklistForTest("bad-plugin");

            Action act = () => manager.CheckNotBlacklisted("bad-plugin");

            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*bad-plugin*");
        }

        [Fact]
        public async Task CheckNotBlacklisted_OtherPluginInBlacklist_DoesNotThrow() {
            var manager = CreateManager();
            await using var _ = manager;
            manager.AddToBlacklistForTest("bad-plugin");

            Action act = () => manager.CheckNotBlacklisted("other-plugin");

            act.Should().NotThrow();
        }

        [Fact]
        public async Task CheckNotBlacklisted_AfterAddThenCheck_IsBlacklistedForTestConsistent() {
            // CheckNotBlacklisted 与 IsBlacklistedForTest 应对同一状态一致
            var manager = CreateManager();
            await using var _ = manager;
            manager.AddToBlacklistForTest("p");

            manager.IsBlacklistedForTest("p").Should().BeTrue();
            Action act = () => manager.CheckNotBlacklisted("p");
            act.Should().Throw<InvalidOperationException>();
        }
    }

    /// <summary>
    /// TransitionFiberTo 确定性测试 — 验证非 WorkflowPluginBase 类型 no-op 不抛
    /// </summary>
    public sealed class TransitionFiberToTest {
        [Fact]
        public void TransitionFiberTo_NullPlugin_DoesNotThrow() {
            // static 方法,传 null 不应 NRE(实际 WorkflowPluginBase 检查会过滤)
            // 注:plugin 参数为 IWorkflowPlugin,null 会匹配 'is WorkflowPluginBase' 为 false,no-op
            Action act = () => PluginManager.TransitionFiberTo(null!, PluginFiberState.Activating);

            act.Should().NotThrow();
        }
    }
}
