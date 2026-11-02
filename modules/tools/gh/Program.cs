namespace JoinCode.Gh;

internal static class Program {
    private static async Task<int> Main(string[] args) {
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
        var ghArgs = new string[args.Length + 1];
        ghArgs[0] = "gh";
        Array.Copy(args, 0, ghArgs, 1, args.Length);
        return await GhSubCommand.ExecuteAsync(ghArgs, cts.Token).ConfigureAwait(false) ?? 1;
    }
}
