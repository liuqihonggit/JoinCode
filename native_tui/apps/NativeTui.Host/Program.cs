namespace NativeTui.Host;

/// <summary>
/// NativeTui 宿主入口 — ADR 0143 阶段二（批次 1 占位：仅保证工程可编译；
/// 批次 3 落地完整组装：raw mode + ReplView + EngineSessionFactory 直连回合驱动）。
/// </summary>
public static class Program {
    /// <summary>入口 — 批次 1 占位。</summary>
    public static int Main(string[] args) {
        Console.Error.WriteLine("NativeTui.Host 组装中（ADR 0143 阶段二批次 3 落地）");
        return 0;
    }
}
