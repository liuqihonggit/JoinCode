namespace Fsm.Generator;

/// <summary>
/// Fsm 源码生成器 — 扫描 [StateMachine] + [Transition] + [Guard] + [TransitionAction] 特性
/// <para>生成 partial class: 转换表 + 每事件独立 C# event + Trigger/CurrentState</para>
/// <para>ADR 0041</para>
/// </summary>
[Generator]
public sealed class FsmGenerator : IIncrementalGenerator
{
    private const string StateMachineAttr = "JoinCode.Abstractions.Attributes.FsmStateMachineAttribute";
    private const string TransitionAttr = "JoinCode.Abstractions.Attributes.TransitionAttribute";
    private const string GuardAttr = "JoinCode.Abstractions.Attributes.GuardAttribute";
    private const string ActionAttr = "JoinCode.Abstractions.Attributes.TransitionActionAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var machines = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                StateMachineAttr,
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, _) => ExtractMachineInfo(ctx))
            .Where(static m => m is not null);

        context.RegisterSourceOutput(machines, static (spc, machine) =>
        {
            if (machine is not null)
                spc.AddSource($"{machine.Namespace}.{machine.ClassName}.Fsm.g.cs", GenerateCode(machine));
        });
    }

    private static MachineInfo? ExtractMachineInfo(GeneratorAttributeSyntaxContext ctx)
    {
        var classDecl = (ClassDeclarationSyntax)ctx.TargetNode;
        var classSymbol = ctx.TargetSymbol as INamedTypeSymbol;
        if (classSymbol is null)
            return null;

        if (ctx.Attributes.Length == 0)
            return null;

        var attr = ctx.Attributes[0];
        if (attr.ConstructorArguments.Length < 3)
            return null;

        var stateType = attr.ConstructorArguments[0].Value as INamedTypeSymbol;
        var eventType = attr.ConstructorArguments[1].Value as INamedTypeSymbol;
        var initialStateValue = attr.ConstructorArguments[2];

        if (stateType is null || eventType is null)
            return null;

        var initialStateName = GetEnumMemberName(initialStateValue);
        if (initialStateName is null)
            return null;

        var ns = classSymbol.ContainingNamespace.IsGlobalNamespace ? "" : classSymbol.ContainingNamespace.ToDisplayString();
        var className = classSymbol.Name;

        var transitions = new List<TransitionInfo>();
        foreach (var a in classSymbol.GetAttributes())
        {
            if (a.AttributeClass?.ToDisplayString() != TransitionAttr)
                continue;
            var from = GetEnumMemberName(a.ConstructorArguments[0]);
            var evt = GetEnumMemberName(a.ConstructorArguments[1]);
            var to = GetEnumMemberName(a.ConstructorArguments[2]);
            if (from is not null && evt is not null && to is not null)
                transitions.Add(new TransitionInfo(from, evt, to));
        }

        var guards = new Dictionary<(string From, string Event), string>();
        var actions = new Dictionary<(string From, string Event), string>();
        foreach (var member in classSymbol.GetMembers())
        {
            if (member is not IMethodSymbol method)
                continue;

            foreach (var a in method.GetAttributes())
            {
                var attrName = a.AttributeClass?.ToDisplayString();
                if (attrName == GuardAttr)
                {
                    var from = GetEnumMemberName(a.ConstructorArguments[0]);
                    var evt = GetEnumMemberName(a.ConstructorArguments[1]);
                    if (from is not null && evt is not null)
                        guards[(from, evt)] = method.Name;
                }
                else if (attrName == ActionAttr)
                {
                    var from = GetEnumMemberName(a.ConstructorArguments[0]);
                    var evt = GetEnumMemberName(a.ConstructorArguments[1]);
                    if (from is not null && evt is not null)
                        actions[(from, evt)] = method.Name;
                }
            }
        }

        var eventValues = new List<string>();
        foreach (var member in eventType.GetMembers().OfType<IFieldSymbol>())
        {
            if (member.ConstantValue is not null)
                eventValues.Add(member.Name);
        }

        return new MachineInfo(ns, className, stateType.Name, eventType.Name, initialStateName, transitions, guards, actions, eventValues);
    }

    private static string? GetEnumMemberName(TypedConstant tc)
    {
        if (tc.Kind != TypedConstantKind.Enum)
            return null;
        if (tc.Type is not INamedTypeSymbol enumType)
            return null;
        var value = tc.Value;
        foreach (var member in enumType.GetMembers().OfType<IFieldSymbol>())
        {
            if (member.ConstantValue?.Equals(value) == true)
                return member.Name;
        }
        return null;
    }

    private static string GenerateCode(MachineInfo m)
    {
        var sb = new StringBuilder();
        sb.AppendLine("#nullable enable");
        if (m.Namespace.Length > 0)
            sb.AppendLine($"namespace {m.Namespace};");
        sb.AppendLine();

        var s = m.StateTypeName;
        var e = m.EventTypeName;

        sb.AppendLine($"partial class {m.ClassName}");
        sb.AppendLine("{");
        sb.AppendLine($"    private static readonly FrozenDictionary<TransitionKey<{s}, {e}>, TransitionRule<{s}>> _fsmTable = FsmBuildTable();");
        sb.AppendLine();

        foreach (var ev in m.EventValues)
            sb.AppendLine($"    internal event EventHandler<TransitionResult<{s}, {e}>>? On{ev};");
        sb.AppendLine();

        sb.AppendLine($"    private void FsmDispatchEvent(TransitionResult<{s}, {e}> e)");
        sb.AppendLine("    {");
        sb.AppendLine("        switch (e.Event)");
        sb.AppendLine("        {");
        foreach (var ev in m.EventValues)
            sb.AppendLine($"            case {e}.{ev}: On{ev}?.Invoke(this, e); break;");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine();

        sb.AppendLine($"    private static FrozenDictionary<TransitionKey<{s}, {e}>, TransitionRule<{s}>> FsmBuildTable()");
        sb.AppendLine("    {");
        sb.AppendLine($"        return new Dictionary<TransitionKey<{s}, {e}>, TransitionRule<{s}>>");
        sb.AppendLine("        {");
        for (var i = 0; i < m.Transitions.Count; i++)
        {
            var t = m.Transitions[i];
            var guard = m.Guards.TryGetValue((t.From, t.Event), out var g) ? g : null;
            var action = m.Actions.TryGetValue((t.From, t.Event), out var a) ? a : null;
            var ruleArgs = $"{s}.{t.To}";
            if (guard is not null && action is not null)
                ruleArgs += $", {guard}, {action}";
            else if (guard is not null)
                ruleArgs += $", {guard}";
            else if (action is not null)
                ruleArgs += $", null, {action}";
            var comma = i < m.Transitions.Count - 1 ? "," : "";
            sb.AppendLine($"            [new({s}.{t.From}, {e}.{t.Event})] = new({ruleArgs}){comma}");
        }
        sb.AppendLine("        }.ToFrozenDictionary();");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private sealed class MachineInfo
    {
        public string Namespace { get; }
        public string ClassName { get; }
        public string StateTypeName { get; }
        public string EventTypeName { get; }
        public string InitialStateName { get; }
        public List<TransitionInfo> Transitions { get; }
        public Dictionary<(string From, string Event), string> Guards { get; }
        public Dictionary<(string From, string Event), string> Actions { get; }
        public List<string> EventValues { get; }

        public MachineInfo(string ns, string className, string stateTypeName, string eventTypeName, string initialStateName,
            List<TransitionInfo> transitions, Dictionary<(string From, string Event), string> guards,
            Dictionary<(string From, string Event), string> actions, List<string> eventValues)
        {
            Namespace = ns;
            ClassName = className;
            StateTypeName = stateTypeName;
            EventTypeName = eventTypeName;
            InitialStateName = initialStateName;
            Transitions = transitions;
            Guards = guards;
            Actions = actions;
            EventValues = eventValues;
        }
    }

    private sealed class TransitionInfo
    {
        public string From { get; }
        public string Event { get; }
        public string To { get; }
        public TransitionInfo(string from, string evt, string to) { From = from; Event = evt; To = to; }
    }
}
