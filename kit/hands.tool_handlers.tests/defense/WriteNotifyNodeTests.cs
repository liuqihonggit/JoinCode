namespace Core.Tests;

public class WriteNotifyNodeTests
{
    private readonly IFileSystem _fs = TestFileSystem.Current;

    [Fact]
    public void NotifyWriteComplete_AllNullDeps_DoesNotThrow()
    {
        var node = new WriteNotifyNode(_fs);

        node.NotifyWriteComplete("/test.txt", "content", "write", FileOperationType.Write);
    }

    [Fact]
    public void NotifyWriteComplete_WithLspDiagnostic_ClearsDelivered()
    {
        var lspDiag = new Mock<ILspDiagnosticProvider>();
        lspDiag.Setup(d => d.ClearDeliveredForFile(It.IsAny<string>()))
               .Verifiable();
        var node = new WriteNotifyNode(_fs, lspDiagnosticProvider: lspDiag.Object);

        node.NotifyWriteComplete("/test.txt", "content", "write", FileOperationType.Write);

        lspDiag.Verify();
    }

    [Fact]
    public void NotifyWriteComplete_WithTelemetry_RecordsCount()
    {
        var telemetry = new Mock<ITelemetryService>();
        var counter = new Mock<ITelemetryCounter>();
        telemetry.Setup(t => t.GetCounter(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>()))
                 .Returns(counter.Object);
        var node = new WriteNotifyNode(_fs, telemetryService: telemetry.Object);

        node.NotifyWriteComplete("/test.txt", "content", "write", FileOperationType.Write);

        telemetry.Verify(t => t.GetCounter("file.operation.count", It.IsAny<string?>(), It.IsAny<string?>()), Times.AtLeastOnce);
        counter.Verify(c => c.Add(1, It.IsAny<Dictionary<string, string>?>()), Times.AtLeastOnce);
    }

    [Fact]
    public void NotifyWriteComplete_WithFileWriteListener_NotifyCalled()
    {
        var listener = new Mock<IFileWriteListenerRegistry>();
        listener.Setup(l => l.Notify(It.IsAny<FileWriteEventArgs>()))
                .Verifiable();
        var node = new WriteNotifyNode(_fs, fileWriteListenerRegistry: listener.Object);

        node.NotifyWriteComplete("/test.txt", "content", "write", FileOperationType.Write);

        listener.Verify();
    }
}
