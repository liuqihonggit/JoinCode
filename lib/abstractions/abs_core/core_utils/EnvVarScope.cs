namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 环境变量临时设置 + 自动恢复作用域。
/// <para>
/// 用法：<c>using var env = EnvVarScope.Set("JCC_LOG_LEVEL", "Error").Add("JCC_DEBUG", "1");</c>
/// 退出 using 作用域时按逆序恢复所有变量原值。消除手写 <c>var prev = Get; Set; try { } finally { Set(prev) }</c> 样板。
/// 详见 ADR-0093、AGENTS.md「代码风格规范」。
/// </para>
/// </summary>
public sealed class EnvVarScope : IDisposable
{
    private readonly List<(string Name, string? Original, EnvironmentVariableTarget Target)> _restored = [];
    private bool _disposed;

    /// <summary>
    /// 创建作用域并设置首个环境变量。返回作用域实例，可链式 <see cref="Add"/> 设置多个。
    /// </summary>
    /// <param name="name">环境变量名。</param>
    /// <param name="value">新值（null 表示删除该变量）。</param>
    /// <param name="target">变量目标（Process/User/Machine），默认 Process。</param>
    public static EnvVarScope Set(string name, string? value, EnvironmentVariableTarget target = EnvironmentVariableTarget.Process)
    {
        var scope = new EnvVarScope();
        scope.Add(name, value, target);
        return scope;
    }

    /// <summary>
    /// 链式追加设置环境变量。记录原值供 Dispose 恢复。
    /// </summary>
    /// <param name="name">环境变量名。</param>
    /// <param name="value">新值（null 表示删除）。</param>
    /// <param name="target">变量目标。</param>
    /// <returns>当前作用域（链式调用）。</returns>
    public EnvVarScope Add(string name, string? value, EnvironmentVariableTarget target = EnvironmentVariableTarget.Process)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(EnvVarScope));
        var original = Environment.GetEnvironmentVariable(name, target);
        _restored.Add((name, original, target));
        Environment.SetEnvironmentVariable(name, value, target);
        return this;
    }

    /// <summary>
    /// 恢复所有环境变量到设置前原值（按设置逆序恢复）。幂等。
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        for (var i = _restored.Count - 1; i >= 0; i--)
        {
            var (name, original, target) = _restored[i];
            Environment.SetEnvironmentVariable(name, original, target);
        }
        _restored.Clear();
    }
}
