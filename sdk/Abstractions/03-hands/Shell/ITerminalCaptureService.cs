namespace JoinCode.Abstractions.Interfaces;

public sealed class TerminalSnapshot
{
    public required string Content { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required DateTime CapturedAt { get; init; }
}

public interface ITerminalCaptureService
{
    TerminalSnapshot CaptureScreen();
    TerminalSnapshot? CaptureBuffer(int maxLines = 50);
}
