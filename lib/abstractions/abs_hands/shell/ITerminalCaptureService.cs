namespace JoinCode.Abstractions.Interfaces;

public sealed class TerminalSnapshot {
    /// <summary>获取快照内容。</summary>
    public required string Content { get; init; }
    /// <summary>获取终端宽度。</summary>
    public required int Width { get; init; }
    /// <summary>获取终端高度。</summary>
    public required int Height { get; init; }
    /// <summary>获取捕获时间。</summary>
    public required DateTime CapturedAt { get; init; }
}

public interface ITerminalCaptureService {
    /// <summary>捕获当前屏幕快照。</summary>
    TerminalSnapshot CaptureScreen();
    /// <summary>捕获缓冲区快照。</summary>
    TerminalSnapshot? CaptureBuffer(int maxLines = 50);
}