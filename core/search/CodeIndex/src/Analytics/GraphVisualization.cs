namespace JoinCode.CodeIndex.Analytics;

/// <summary>
/// 图可视化实现 — 导出为 DOT(Graphviz) 和 HTML(D3.js 力导向图)
/// </summary>
[Register]
public sealed class GraphVisualization : IGraphVisualization
{
    private readonly InMemoryIndexStore _store;

    public GraphVisualization(InMemoryIndexStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    public Task<string> ExportDotAsync(CancellationToken ct)
    {
        using var scope = _store.EnterReadLock();
        return Task.FromResult(BuildDot(_store.CallEdges, "CallGraph"));
    }

    public Task<string> ExportHtmlAsync(CancellationToken ct)
    {
        using var scope = _store.EnterReadLock();
        return Task.FromResult(BuildHtml(_store.CallEdges));
    }

    public Task<string> ExportSubgraphDotAsync(string centerSymbol, int hops, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(centerSymbol);
        using var scope = _store.EnterReadLock();

        var nodes = new HashSet<string>(StringComparer.Ordinal) { centerSymbol };
        var edges = new List<CallEdge>();
        var frontier = new HashSet<string>(StringComparer.Ordinal) { centerSymbol };

        for (int i = 0; i < hops && frontier.Count > 0; i++)
        {
            var nextFrontier = new HashSet<string>(StringComparer.Ordinal);
            foreach (var sym in frontier)
            {
                if (_store.CallsByCaller.TryGetValue(sym, out var callees))
                    foreach (var e in callees)
                    {
                        edges.Add(e);
                        if (nodes.Add(e.CalleeSymbol)) nextFrontier.Add(e.CalleeSymbol);
                    }
                if (_store.CallsByCallee.TryGetValue(sym, out var callers))
                    foreach (var e in callers)
                    {
                        edges.Add(e);
                        if (nodes.Add(e.CallerSymbol)) nextFrontier.Add(e.CallerSymbol);
                    }
            }
            frontier = nextFrontier;
        }

        return Task.FromResult(BuildDot(edges, $"Subgraph_{centerSymbol}"));
    }

    private static string BuildDot(List<CallEdge> edges, string title)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"digraph \"{title}\" {{");
        sb.AppendLine("  rankdir=TB;");
        sb.AppendLine("  node [shape=box,fontname=\"Consolas\"];");
        sb.AppendLine();

        var nodeIds = new Dictionary<string, string>(StringComparer.Ordinal);
        var id = 0;
        foreach (var edge in edges)
        {
            if (!nodeIds.ContainsKey(edge.CallerSymbol))
                nodeIds[edge.CallerSymbol] = $"n{id++}";
            if (!nodeIds.ContainsKey(edge.CalleeSymbol))
                nodeIds[edge.CalleeSymbol] = $"n{id++}";
        }

        foreach (var kvp in nodeIds)
            sb.AppendLine($"  {kvp.Value} [label=\"{EscapeDot(kvp.Key)}\"];");

        sb.AppendLine();
        foreach (var edge in edges)
        {
            var from = nodeIds[edge.CallerSymbol];
            var to = nodeIds[edge.CalleeSymbol];
            sb.AppendLine($"  {from} -> {to} [label=\"{edge.CallKind}\"];");
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string BuildHtml(List<CallEdge> edges)
    {
        var nodes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in edges) { nodes.Add(e.CallerSymbol); nodes.Add(e.CalleeSymbol); }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html><head><meta charset=\"utf-8\"><title>Call Graph</title>");
        sb.AppendLine("<style>body{margin:0;font-family:Consolas,monospace;} svg{width:100%;height:100vh;}</style>");
        sb.AppendLine("<script src=\"https://d3js.org/d3.v7.min.js\"></script>");
        sb.AppendLine("</head><body><script>");
        sb.AppendLine("const data={nodes:[");
        var nodeList = nodes.ToList();
        for (int i = 0; i < nodeList.Count; i++)
            sb.AppendLine($"{{id:\"{EscapeJs(nodeList[i])}\",group:1}},");
        sb.AppendLine("],links:[");
        foreach (var e in edges)
            sb.AppendLine($"{{source:\"{EscapeJs(e.CallerSymbol)}\",target:\"{EscapeJs(e.CalleeSymbol)}\"}},");
        sb.AppendLine("]};");
        sb.AppendLine("const w=window.innerWidth,h=window.innerHeight;");
        sb.AppendLine("const svg=d3.select('body').append('svg').attr('width',w).attr('height',h);");
        sb.AppendLine("const sim=d3.forceSimulation(data.nodes).force('link',d3.forceLink(data.links).id(d=>d.id)).force('charge',d3.forceManyBody().strength(-200)).force('center',d3.forceCenter(w/2,h/2));");
        sb.AppendLine("const link=svg.append('g').selectAll('line').data(data.links).join('line').attr('stroke','#999').attr('stroke-opacity',0.6);");
        sb.AppendLine("const node=svg.append('g').selectAll('circle').data(data.nodes).join('circle').attr('r',5).attr('fill','#69b3a2').call(d3.drag().on('start',d=>{if(!d.active)sim.alphaTarget(0.3).restart();d.fx=d.x;d.fy=d.y;}).on('drag',d=>{d.fx=d3.event.x;d.fy=d3.event.y;}).on('end',d=>{if(!d.active)sim.alphaTarget(0);d.fx=null;d.fy=null;}));");
        sb.AppendLine("sim.on('tick',()=>{link.attr('x1',d=>d.source.x).attr('y1',d=>d.source.y).attr('x2',d=>d.target.x).attr('y2',d=>d.target.y);node.attr('cx',d=>d.x).attr('cy',d=>d.y);});");
        sb.AppendLine("</script></body></html>");
        return sb.ToString();
    }

    private static string EscapeDot(string s) => s.Replace("\"", "\\\"");
    private static string EscapeJs(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("'", "\\'");
}
