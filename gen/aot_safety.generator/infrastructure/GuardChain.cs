namespace AotSafety.Generator.Infrastructure;

public sealed class GuardChain {
    private bool _failed;

    private GuardChain(bool failed) {
        _failed = failed;
    }

    public static GuardChain Create() => new(false);
    public bool Failed => _failed;

    public GuardChain Require(bool condition) {
        if (!condition) _failed = true;
        return this;
    }

    public GuardChain RequireNotCancellationRequested(System.Threading.CancellationToken token) {
        if (token.IsCancellationRequested) _failed = true;
        return this;
    }
}
