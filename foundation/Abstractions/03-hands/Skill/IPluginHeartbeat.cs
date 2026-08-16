namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 插件心跳接口 — 惰性存活检测
/// <para>使用上层插件资源时,先调用 EnsureAlive 检测心跳</para>
/// <para>心跳停止表示插件死亡,本层自然死亡,下层也通知死亡</para>
/// <para>只有使用时触发,不主动轮询 — 零开销,读 volatile bool 纳秒级</para>
/// </summary>
public interface IPluginHeartbeat
{
    /// <summary>是否存活 — volatile bool,纳秒级读取</summary>
    bool IsAlive { get; }

    /// <summary>最后心跳时刻</summary>
    DateTime LastHeartbeatAt { get; }

    /// <summary>刷新心跳 — 插件每次活动时调用</summary>
    void Touch();

    /// <summary>标记死亡 — 不可逆,触发 OnDeath 事件</summary>
    void MarkDead();

    /// <summary>死亡事件 — 心跳停止时触发,下层据此通知死亡</summary>
    event EventHandler? OnDeath;
}
