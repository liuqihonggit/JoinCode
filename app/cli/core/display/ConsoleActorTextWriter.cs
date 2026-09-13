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

    /// <summary>
    /// 构造函数 — 绑定目标 ConsoleActor 实例
    /// </summary>
    /// <param name="actor">接收所有写操作的 ConsoleActor</param>
    public ConsoleActorTextWriter(ConsoleActor actor)
    {
        _actor = actor;
    }

    /// <summary>
    /// 获取输出编码 — 固定返回 UTF-8
    /// </summary>
    public override Encoding Encoding => System.Text.Encoding.UTF8;

    /// <summary>
    /// 将字符串写入 ConsoleActor
    /// </summary>
    /// <param name="value">要写入的字符串；为 null 时写空字符串</param>
    public override void Write(string? value)
    {
        _actor.WriteRaw(value ?? string.Empty);
    }

    /// <summary>
    /// 将字符串加换行写入 ConsoleActor
    /// </summary>
    /// <param name="value">要写入的字符串</param>
    public override void WriteLine(string? value)
    {
        _actor.WriteLine(value);
    }

    /// <summary>
    /// 向 ConsoleActor 写入一个空行
    /// </summary>
    public override void WriteLine()
    {
        _actor.WriteLine();
    }

    /// <summary>
    /// 将单个字符写入 ConsoleActor
    /// </summary>
    /// <param name="value">要写入的字符</param>
    public override void Write(char value)
    {
        _actor.WriteRaw(new string(value, 1));
    }

    /// <summary>
    /// 将字符数组的指定区间写入 ConsoleActor
    /// </summary>
    /// <param name="buffer">字符数据源</param>
    /// <param name="index">起始索引</param>
    /// <param name="count">要写入的字符数</param>
    public override void Write(char[] buffer, int index, int count)
    {
        _actor.WriteRaw(new string(buffer, index, count));
    }

    /// <summary>
    /// 将只读字符跨度写入 ConsoleActor
    /// </summary>
    /// <param name="buffer">要写入的字符跨度</param>
    public override void Write(ReadOnlySpan<char> buffer)
    {
        _actor.WriteRaw(new string(buffer));
    }

    /// <summary>
    /// 将 StringBuilder 内容写入 ConsoleActor
    /// </summary>
    /// <param name="value">要写入的 StringBuilder；为 null 时写空字符串</param>
    public override void Write(StringBuilder? value)
    {
        _actor.WriteRaw(value?.ToString() ?? string.Empty);
    }

    /// <summary>
    /// 刷新 ConsoleActor 的输出缓冲
    /// </summary>
    public override void Flush()
    {
        _actor.Flush();
    }
}
