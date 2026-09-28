namespace StructuraBenchmarks;

/// <summary>ImmutableDag vs Dag vs ConcurrentDag 压测 — 构建+拓扑排序+查询，对比无锁 CAS vs 可变 Dictionary vs 锁保护。</summary>
[MemoryDiagnoser]
[ShortRunJob]
public class DagBench {
    [Params(1000, 5000)]
    public int Size { get; set; }

    private ImmutableDag<string> _immutableDag = null!;
    private Dag<string> _dag = null!;
    private ConcurrentDag<string> _concurrentDag = null!;
    private string[] _nodeIds = null!;

    [GlobalSetup]
    public void Setup() {
        _nodeIds = new string[Size];
        for (var i = 0; i < Size; i++) _nodeIds[i] = $"n{i}";

        _immutableDag = ImmutableDag<string>.Empty;
        for (var i = 0; i < Size; i++) _immutableDag.AddNode(new ImmutableDagNode<string> { Id = _nodeIds[i], Payload = _nodeIds[i] });
        for (var i = 0; i < Size - 1; i++) _immutableDag.AddEdge(new DagEdge { FromId = _nodeIds[i], ToId = _nodeIds[i + 1] });

        _dag = new Dag<string>();
        for (var i = 0; i < Size; i++) _dag.AddNode(new DagNode<string> { Id = _nodeIds[i], Payload = _nodeIds[i] });
        for (var i = 0; i < Size - 1; i++) _dag.AddEdge(new DagEdge { FromId = _nodeIds[i], ToId = _nodeIds[i + 1] });

        _concurrentDag = new ConcurrentDag<string>();
        for (var i = 0; i < Size; i++) _concurrentDag.AddNode(new DagNode<string> { Id = _nodeIds[i], Payload = _nodeIds[i] });
        for (var i = 0; i < Size - 1; i++) _concurrentDag.AddEdge(new DagEdge { FromId = _nodeIds[i], ToId = _nodeIds[i + 1] });
    }

    [GlobalCleanup]
    public void Cleanup() => _concurrentDag.Dispose();

    [Benchmark(Description = "Build ImmutableDag (HAMT+CAS)")]
    public void Build_ImmutableDag() {
        var dag = ImmutableDag<string>.Empty;
        for (var i = 0; i < Size; i++) dag.AddNode(new ImmutableDagNode<string> { Id = _nodeIds[i], Payload = _nodeIds[i] });
        for (var i = 0; i < Size - 1; i++) dag.AddEdge(new DagEdge { FromId = _nodeIds[i], ToId = _nodeIds[i + 1] });
    }

    [Benchmark(Description = "Build Dag (Dictionary)")]
    public void Build_Dag() {
        var dag = new Dag<string>();
        for (var i = 0; i < Size; i++) dag.AddNode(new DagNode<string> { Id = _nodeIds[i], Payload = _nodeIds[i] });
        for (var i = 0; i < Size - 1; i++) dag.AddEdge(new DagEdge { FromId = _nodeIds[i], ToId = _nodeIds[i + 1] });
    }

    [Benchmark(Description = "Build ConcurrentDag (SemaphoreSlim)")]
    public void Build_ConcurrentDag() {
        using var dag = new ConcurrentDag<string>();
        for (var i = 0; i < Size; i++) dag.AddNode(new DagNode<string> { Id = _nodeIds[i], Payload = _nodeIds[i] });
        for (var i = 0; i < Size - 1; i++) dag.AddEdge(new DagEdge { FromId = _nodeIds[i], ToId = _nodeIds[i + 1] });
    }

    [Benchmark(Description = "TopoSort ImmutableDag")]
    public int TopologicalSort_ImmutableDag() => _immutableDag.TopologicalSort().Count;

    [Benchmark(Description = "TopoSort Dag")]
    public int TopologicalSort_Dag() => _dag.TopologicalSort().Count;

    [Benchmark(Description = "GetDescendants ImmutableDag")]
    public int GetDescendants_ImmutableDag() => _immutableDag.GetDescendants(_nodeIds[0]).Count();

    [Benchmark(Description = "GetDescendants Dag")]
    public int GetDescendants_Dag() => _dag.GetDescendants(_nodeIds[0]).Count();
}
