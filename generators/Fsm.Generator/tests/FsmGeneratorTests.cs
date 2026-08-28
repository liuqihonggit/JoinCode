namespace Fsm.Generator.Tests;

/// <summary>
/// FsmGenerator 源码生成器单元测试
/// <para>验证生成器正确生成: 排序数组 + 每事件独立 event + FsmDispatchEvent + 守卫/动作关联</para>
/// <para>ADR 0041</para>
/// </summary>
public class FsmGeneratorTests
{
    private const string SimpleMachineSource = """
        using JoinCode.Abstractions.Attributes;

        namespace TestApp;

        public enum TestState { Idle, Running, Done }
        public enum TestEvent { Start, Complete }

        [FsmStateMachine(typeof(TestState), typeof(TestEvent), TestState.Idle)]
        [Transition(TestState.Idle, TestEvent.Start, TestState.Running)]
        [Transition(TestState.Running, TestEvent.Complete, TestState.Done)]
        public partial class TestMachine
        {
        }
        """;

    [Fact]
    public void SimpleTransition_GeneratesSortedKeysArray()
    {
        var code = TestHelper.RunGenerator(SimpleMachineSource).GeneratedCode;
        code.Should().Contain("_fsmSortedKeys");
        code.Should().Contain("FsmBuildSortedKeys");
        code.Should().Contain("new(TestState.Idle, TestEvent.Start)");
        code.Should().Contain("new(TestState.Running, TestEvent.Complete)");
    }

    [Fact]
    public void SimpleTransition_GeneratesRulesArray()
    {
        var code = TestHelper.RunGenerator(SimpleMachineSource).GeneratedCode;
        code.Should().Contain("_fsmRules");
        code.Should().Contain("FsmBuildRules");
        code.Should().Contain("new(TestState.Running)");
        code.Should().Contain("new(TestState.Done)");
    }

    [Fact]
    public void GeneratesEventForEachEventEnumValue()
    {
        var code = TestHelper.RunGenerator(SimpleMachineSource).GeneratedCode;
        code.Should().Contain("event EventHandler<TransitionResult<TestState, TestEvent>>? OnStart;");
        code.Should().Contain("event EventHandler<TransitionResult<TestState, TestEvent>>? OnComplete;");
    }

    [Fact]
    public void FsmDispatchEvent_SwitchCoversAllEvents()
    {
        var code = TestHelper.RunGenerator(SimpleMachineSource).GeneratedCode;
        code.Should().Contain("switch (e.Event)");
        code.Should().Contain("case TestEvent.Start: OnStart?.Invoke(this, e); break;");
        code.Should().Contain("case TestEvent.Complete: OnComplete?.Invoke(this, e); break;");
    }

    [Fact]
    public void Transitions_AreSortedByFromThenEvent()
    {
        var unsortedSource = """
            using JoinCode.Abstractions.Attributes;

            namespace TestApp;

            public enum St { A, B, C }
            public enum Ev { X, Y }

            [FsmStateMachine(typeof(St), typeof(Ev), St.A)]
            [Transition(St.C, Ev.Y, St.A)]
            [Transition(St.A, Ev.Y, St.B)]
            [Transition(St.A, Ev.X, St.C)]
            [Transition(St.B, Ev.X, St.C)]
            public partial class UnsortedMachine
            {
            }
            """;
        var code = TestHelper.RunGenerator(unsortedSource).GeneratedCode;
        var lines = code.Split('\n').Select(l => l.Trim()).ToArray();
        var keyLines = lines.Where(l => l.StartsWith("new(St.") && l.Contains("Ev.")).ToArray();
        keyLines.Should().HaveCount(4);
        keyLines[0].Should().Contain("St.A").And.Contain("Ev.X");
        keyLines[1].Should().Contain("St.A").And.Contain("Ev.Y");
        keyLines[2].Should().Contain("St.B").And.Contain("Ev.X");
        keyLines[3].Should().Contain("St.C").And.Contain("Ev.Y");
    }

    [Fact]
    public void GuardMethod_IsReferencedInRule()
    {
        var source = """
            using JoinCode.Abstractions.Attributes;
            using JoinCode.Abstractions.Utils.State;

            namespace TestApp;

            public enum GState { Idle, Active }
            public enum GEvent { Go }

            [FsmStateMachine(typeof(GState), typeof(GEvent), GState.Idle)]
            [Transition(GState.Idle, GEvent.Go, GState.Active)]
            public partial class GuardedMachine
            {
                [Guard(GState.Idle, GEvent.Go)]
                private static bool CanGo(FsmContext? ctx) => true;
            }
            """;
        var code = TestHelper.RunGenerator(source).GeneratedCode;
        code.Should().Contain("CanGo");
        code.Should().Contain("new(GState.Active, CanGo)");
    }

    [Fact]
    public void TransitionActionMethod_IsReferencedInRule()
    {
        var source = """
            using JoinCode.Abstractions.Attributes;
            using JoinCode.Abstractions.Utils.State;

            namespace TestApp;

            public enum AState { Idle, Done }
            public enum AEvent { Fire }

            [FsmStateMachine(typeof(AState), typeof(AEvent), AState.Idle)]
            [Transition(AState.Idle, AEvent.Fire, AState.Done)]
            public partial class ActionMachine
            {
                [TransitionAction(AState.Idle, AEvent.Fire)]
                private static void OnFire(FsmContext? ctx) { }
            }
            """;
        var code = TestHelper.RunGenerator(source).GeneratedCode;
        code.Should().Contain("OnFire");
        code.Should().Contain("null, OnFire");
    }

    [Fact]
    public void GuardAndAction_BothReferencedInRule()
    {
        var source = """
            using JoinCode.Abstractions.Attributes;
            using JoinCode.Abstractions.Utils.State;

            namespace TestApp;

            public enum S { Idle, Done }
            public enum E { Fire }

            [FsmStateMachine(typeof(S), typeof(E), S.Idle)]
            [Transition(S.Idle, E.Fire, S.Done)]
            public partial class FullMachine
            {
                [Guard(S.Idle, E.Fire)]
                private static bool CanFire(FsmContext? ctx) => true;

                [TransitionAction(S.Idle, E.Fire)]
                private static void DoFire(FsmContext? ctx) { }
            }
            """;
        var code = TestHelper.RunGenerator(source).GeneratedCode;
        code.Should().Contain("new(S.Done, CanFire, DoFire)");
    }

    [Fact]
    public void NoTransitions_GeneratesEmptyArrays()
    {
        var source = """
            using JoinCode.Abstractions.Attributes;

            namespace TestApp;

            public enum EState { Only }
            public enum EEvent { None }

            [FsmStateMachine(typeof(EState), typeof(EEvent), EState.Only)]
            public partial class EmptyMachine
            {
            }
            """;
        var code = TestHelper.RunGenerator(source).GeneratedCode;
        code.Should().Contain("TransitionKey<EState, EEvent>[]");
        code.Should().Contain("TransitionRule<EState>[]");
        code.Should().Contain("event EventHandler<TransitionResult<EState, EEvent>>? OnNone;");
    }

    [Fact]
    public void GeneratedFile_HasCorrectHintName()
    {
        var file = TestHelper.RunGeneratorAndGetFile(SimpleMachineSource, "TestApp.TestMachine.Fsm.g.cs");
        file.Should().NotBeNull("生成器应输出 TestApp.TestMachine.Fsm.g.cs");
    }

    [Fact]
    public void GeneratedCode_HasNullableEnable()
    {
        var code = TestHelper.RunGenerator(SimpleMachineSource).GeneratedCode;
        code.Should().Contain("#nullable enable");
    }

    [Fact]
    public void GeneratedCode_HasNamespace()
    {
        var code = TestHelper.RunGenerator(SimpleMachineSource).GeneratedCode;
        code.Should().Contain("namespace TestApp;");
    }

    [Fact]
    public void GeneratedCode_HasPartialClass()
    {
        var code = TestHelper.RunGenerator(SimpleMachineSource).GeneratedCode;
        code.Should().Contain("partial class TestMachine");
    }
}
