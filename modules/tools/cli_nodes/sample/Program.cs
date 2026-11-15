namespace JoinCode.CliNode.Sample;

internal static class Program {
    private static async Task<int> Main(string[] args) {
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
        return await new SampleNode().ExecuteAsync(args, cts.Token).ConfigureAwait(false);
    }
}
