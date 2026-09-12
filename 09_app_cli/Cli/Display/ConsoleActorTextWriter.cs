namespace JoinCode.Cli.Display;

/// <summary>
/// TextWriter 适配器 — 将 Console.Out 的所有输出转发到 ConsoleActor 串行化。
/// <para>在 TerminalHelper.Init() 时通过 Console.SetOut() 安装，使所有经过 Console.Out 的输出</para>
/// <para>（包括 ILogger→AddConsole、TerminalHelper.WriteLine、ConsoleOutput.WriteLine 等）自动经过 Actor。</para>
/// <para>架构决策见 ADR 0100。</para>
/// </summary>
public sealed class ConsoleActorTextWriter : TextWriter
{
    private readonly ConsoleActor _actor;

    public ConsoleActorTextWriter(ConsoleActor actor)
    {
        _actor = actor;
    }

    public override Encoding Encoding => System.Text.Encoding.UTF8;

    public override void Write(string? value)
    {
        _actor.WriteRaw(value ?? string.Empty);
    }

    public override void WriteLine(string? value)
    {
        _actor.WriteLine(value);
    }

    public override void WriteLine()
    {
        _actor.WriteLine();
    }

    public override void Write(char value)
    {
        _actor.WriteRaw(new string(value, 1));
    }

    public override void Write(char[] buffer, int index, int count)
    {
        _actor.WriteRaw(new string(buffer, index, count));
    }

    public override void Write(ReadOnlySpan<char> buffer)
    {
        _actor.WriteRaw(new string(buffer));
    }

    public override void Write(StringBuilder? value)
    {
        _actor.WriteRaw(value?.ToString() ?? string.Empty);
    }

    public override void Flush()
    {
        _actor.Flush();
    }
}
