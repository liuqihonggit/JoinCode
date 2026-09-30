namespace JoinCode.Abstractions.CodeIndex;

/// <summary>
/// 统一索引存储接口 — 继承 IBinaryPersistence，补充 Kind/Count/IsReady 通用属性。
/// <para>符号索引、向量索引、父文档存储均实现此接口。</para>
/// <para>消费者用 List&lt;IIndexStore&gt; + LINQ 链式统一加载/保存，用 IndexKind 位运算过滤。</para>
/// <para>通用函数（Save/Load/Exists/Count/IsReady）在接口上，差异函数（搜索）各自在实现类上。</para>
/// </summary>
public interface IIndexStore : IBinaryPersistence {
    /// <summary>索引类型标识 — 用于位运算过滤。</summary>
    IndexKind Kind { get; }

    /// <summary>当前索引项数量。</summary>
    int Count { get; }

    /// <summary>是否已就绪可查询。</summary>
    bool IsReady { get; }
}
