namespace JoinCode.Rg;

internal static class Program {
    private static async Task<int> Main(string[] args) {
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
        var rgArgs = new string[args.Length + 1];
        rgArgs[0] = "rg";
        Array.Copy(args, 0, rgArgs, 1, args.Length);
        return await RgSubCommand.ExecuteAsync(rgArgs, cts.Token).ConfigureAwait(false) ?? 1;
    }
}
