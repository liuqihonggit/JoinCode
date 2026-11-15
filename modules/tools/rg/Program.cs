namespace JoinCode.Rg;

internal static class Program {
    private static async Task<int> Main(string[] args) {
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
        return await new RgCliToolNode().ExecuteAsync(args, cts.Token).ConfigureAwait(false);
    }
}
