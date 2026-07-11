
namespace Integration.Tests.Capture;

/// <summary>
/// ANSI 输出解析器 — 用于测试中解析终端输出
/// </summary>
public sealed class AnsiOutputParser
{
    public List<AnsiSegment> Parse(string output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var segments = new List<AnsiSegment>();
        var pos = 0;

        while (pos < output.Length)
        {
            if (output[pos] == '\x1b' && pos + 1 < output.Length && output[pos + 1] == '[')
            {
                var seqStart = pos;
                pos += 2;
                while (pos < output.Length && !char.IsLetter(output[pos]))
                    pos++;
                if (pos < output.Length) pos++;

                var sequence = output[seqStart..pos];
                segments.Add(new AnsiSegment(sequence, SegmentType.EscapeSequence, null));
            }
            else if (output[pos] == '\n')
            {
                segments.Add(new AnsiSegment("\n", SegmentType.NewLine, null));
                pos++;
            }
            else if (output[pos] == '\r')
            {
                pos++;
            }
            else
            {
                var textStart = pos;
                while (pos < output.Length && output[pos] != '\x1b' && output[pos] != '\n' && output[pos] != '\r')
                    pos++;

                var text = output[textStart..pos];
                segments.Add(new AnsiSegment(text, SegmentType.Text, ClassifyText(text)));
            }
        }

        return segments;
    }

    private static TextKind? ClassifyText(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0) return null;

        if (trimmed is "●" or "✓" or "✗" or "❯")
            return TextKind.Icon;
        if (IsOptionText(trimmed))
            return TextKind.Option;
        if (trimmed.StartsWith('+') || trimmed.StartsWith('-'))
            return TextKind.DiffMarker;
        if (int.TryParse(trimmed, out _))
            return TextKind.LineNumber;

        return TextKind.Plain;
    }

    private static bool IsOptionText(string text)
    {
        var options = new[]
        {
            "Yes", "No", "Always", "Allow",
            "是", "否", "始终", "允许",
            "始终允许", "始终允许此命令", "始终允许此域名"
        };
        foreach (var opt in options)
        {
            if (text.Equals(opt, StringComparison.Ordinal))
                return true;
            if (text.EndsWith(opt, StringComparison.Ordinal) && text.Length > opt.Length)
                return true;
        }
        return false;
    }

    public ParsedAnsiOutput ToStructured(List<AnsiSegment> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);
        var output = new ParsedAnsiOutput();
        var currentStyles = new HashSet<string>();

        foreach (var seg in segments)
        {
            if (seg.Type == SegmentType.EscapeSequence)
            {
                var style = ClassifyEscapeSequence(seg.Content);
                if (style == "reset")
                    currentStyles.Clear();
                else if (style is not null)
                    currentStyles.Add(style);
            }
            else if (seg.Type == SegmentType.Text && seg.TextKind is not null)
            {
                output.Elements.Add(new AnsiElement(
                    seg.Content,
                    seg.TextKind.Value,
                    currentStyles.ToFrozenSet()
                ));
            }
        }

        return output;
    }

    private static string? ClassifyEscapeSequence(string seq)
    {
        if (seq == "\x1b[0m") return "reset";
        if (seq == "\x1b[1m") return "bold";
        if (seq == "\x1b[2m") return "dim";
        if (seq == "\x1b[3m") return "italic";
        if (seq == "\x1b[4m") return "underline";
        if (seq == "\x1b[7m") return "reverse";
        if (seq.StartsWith("\x1b[38;2;")) return "fg-rgb";
        if (seq.StartsWith("\x1b[48;2;")) return "bg-rgb";
        if (seq.StartsWith("\x1b[3") && seq.EndsWith('m')) return "fg-basic";
        if (seq.StartsWith("\x1b[4") && seq.EndsWith('m')) return "bg-basic";
        return null;
    }
}

public readonly record struct AnsiSegment(string Content, SegmentType Type, TextKind? TextKind);

public enum SegmentType : byte
{
    EscapeSequence,
    Text,
    NewLine
}

public enum TextKind : byte
{
    Plain,
    Icon,
    Option,
    DiffMarker,
    LineNumber
}

public sealed record ParsedAnsiOutput
{
    public List<AnsiElement> Elements { get; } = [];
}

public sealed record AnsiElement(string Text, TextKind Kind, FrozenSet<string> Styles);
