namespace Cbm.Store;

internal static class LeidenClustering
{
    private const int MaxLevels = 64;
    private const int MovePassCap = 100;

    public static int[] DetectCommunities(
        ReadOnlySpan<long> nodeIds,
        ReadOnlySpan<(long Src, long Dst)> edges,
        double resolution = 1.0)
    {
        var n = nodeIds.Length;
        if (n <= 0)
        {
            return Array.Empty<int>();
        }

        var gamma = resolution > 0.0 ? resolution : 1.0;
        var communities = new int[n];
        for (var i = 0; i < n; i++)
        {
            communities[i] = i;
        }

        BuildWeights(nodeIds, n, edges, out var wsi, out var wdi, out var ww, out var wn);
        if (wn <= 0 || !LgBuild(n, wsi, wdi, ww, wn, out var graph))
        {
            return communities;
        }

        var twom = 0.0;
        for (var i = 0; i < graph.N; i++)
        {
            twom += graph.K[i];
        }

        if (twom <= 0.0)
        {
            graph.Dispose();
            return communities;
        }

        var orig = new int[n];
        var comm = new int[graph.N];
        for (var i = 0; i < n; i++)
        {
            orig[i] = i;
        }

        for (var i = 0; i < graph.N; i++)
        {
            comm[i] = i;
        }

        for (var level = 0; level < MaxLevels; level++)
        {
            LeidenMove(ref graph, comm, gamma, twom);
            var cCount = LeidenRelabel(comm, graph.N);
            if (cCount >= graph.N)
            {
                break;
            }

            var refined = new int[graph.N];
            var rCount = LeidenRefine(ref graph, comm, gamma, twom, refined);
            if (rCount >= graph.N)
            {
                break;
            }

            for (var i = 0; i < n; i++)
            {
                orig[i] = refined[orig[i]];
            }

            if (!LeidenAggregate(ref graph, refined, rCount, comm, out var g2, out var seed))
            {
                break;
            }

            graph.Dispose();
            graph = g2;
            comm = seed;
        }

        var result = new int[n];
        for (var i = 0; i < n; i++)
        {
            result[i] = comm[orig[i]];
        }

        graph.Dispose();
        return result;
    }

    private static void BuildWeights(
        ReadOnlySpan<long> nodes,
        int n,
        ReadOnlySpan<(long Src, long Dst)> edges,
        out int[] wsi,
        out int[] wdi,
        out double[] ww,
        out int wn)
    {
        var wcap = edges.Length > 0 ? edges.Length : 1;
        wsi = new int[wcap];
        wdi = new int[wcap];
        ww = new double[wcap];
        wn = 0;

        for (var e = 0; e < edges.Length; e++)
        {
            var si = NodeIndex(nodes, n, edges[e].Src);
            var di = NodeIndex(nodes, n, edges[e].Dst);
            if (si < 0 || di < 0 || si == di)
            {
                continue;
            }

            if (si > di)
            {
                (si, di) = (di, si);
            }

            var found = -1;
            for (var i = 0; i < wn; i++)
            {
                if (wsi[i] == si && wdi[i] == di)
                {
                    found = i;
                    break;
                }
            }

            if (found >= 0)
            {
                ww[found] += 1.0;
            }
            else
            {
                if (wn >= wcap)
                {
                    wcap *= 2;
                    Array.Resize(ref wsi, wcap);
                    Array.Resize(ref wdi, wcap);
                    Array.Resize(ref ww, wcap);
                }

                wsi[wn] = si;
                wdi[wn] = di;
                ww[wn] = 1.0;
                wn++;
            }
        }
    }

    private static int NodeIndex(ReadOnlySpan<long> nodes, int n, long id)
    {
        for (var i = 0; i < n; i++)
        {
            if (nodes[i] == id)
            {
                return i;
            }
        }

        return -1;
    }

    private sealed class LgGraph : IDisposable
    {
        public int N { get; set; }
        public int[] Off { get; set; } = Array.Empty<int>();
        public int[] Nbr { get; set; } = Array.Empty<int>();
        public double[] W { get; set; } = Array.Empty<double>();
        public double[] K { get; set; } = Array.Empty<double>();

        public void Dispose()
        {
            Off = Array.Empty<int>();
            Nbr = Array.Empty<int>();
            W = Array.Empty<double>();
            K = Array.Empty<double>();
        }
    }

    private static bool LgBuild(int n, int[] wsi, int[] wdi, double[] ww, int wn, out LgGraph graph)
    {
        graph = new LgGraph { N = n };
        var off = new int[n + 1];
        var k = new double[n];
        var fill = new int[n];

        for (var e = 0; e < wn; e++)
        {
            off[wsi[e] + 1]++;
            off[wdi[e] + 1]++;
        }

        for (var i = 0; i < n; i++)
        {
            off[i + 1] += off[i];
        }

        var total = off[n];
        var nbr = new int[Math.Max(total, 1)];
        var w = new double[Math.Max(total, 1)];
        Array.Copy(off, fill, n);

        for (var e = 0; e < wn; e++)
        {
            var a = wsi[e];
            var b = wdi[e];
            var we = ww[e];
            nbr[fill[a]] = b;
            w[fill[a]] = we;
            fill[a]++;
            nbr[fill[b]] = a;
            w[fill[b]] = we;
            fill[b]++;
            k[a] += we;
            k[b] += we;
        }

        graph.Off = off;
        graph.Nbr = nbr;
        graph.W = w;
        graph.K = k;
        return true;
    }

    private static void LeidenMove(ref LgGraph g, int[] comm, double gamma, double twom)
    {
        var n = g.N;
        var stot = new double[n];
        var acc = new double[n];
        var queue = new int[n];
        var dirty = new int[n];
        var inq = new bool[n];

        for (var i = 0; i < n; i++)
        {
            stot[comm[i]] += g.K[i];
            queue[i] = i;
            inq[i] = true;
        }

        var qhead = 0;
        var qcount = n;
        long cap = (long)n * MovePassCap + MaxLevels;

        while (qcount > 0 && cap-- > 0)
        {
            var v = queue[qhead];
            qhead = (qhead + 1) % n;
            qcount--;
            inq[v] = false;
            var cv = comm[v];
            var ndirty = 0;

            for (var e = g.Off[v]; e < g.Off[v + 1]; e++)
            {
                var u = g.Nbr[e];
                if (u == v)
                {
                    continue;
                }

                var cu = comm[u];
                if (acc[cu] == 0.0)
                {
                    dirty[ndirty++] = cu;
                }

                acc[cu] += g.W[e];
            }

            stot[cv] -= g.K[v];
            var kv = g.K[v];
            var bestC = cv;
            var bestGain = acc[cv] - gamma * kv * stot[cv] / twom;

            for (var d = 0; d < ndirty; d++)
            {
                var c = dirty[d];
                var gain = acc[c] - gamma * kv * stot[c] / twom;
                if (gain > bestGain)
                {
                    bestGain = gain;
                    bestC = c;
                }
            }

            stot[bestC] += kv;
            comm[v] = bestC;

            if (bestC != cv)
            {
                for (var e = g.Off[v]; e < g.Off[v + 1]; e++)
                {
                    var u = g.Nbr[e];
                    if (comm[u] != bestC && !inq[u] && qcount < n)
                    {
                        queue[(qhead + qcount) % n] = u;
                        qcount++;
                        inq[u] = true;
                    }
                }
            }

            for (var d = 0; d < ndirty; d++)
            {
                acc[dirty[d]] = 0.0;
            }
        }
    }

    private static int LeidenRelabel(int[] comm, int n)
    {
        var map = new int[n];
        Array.Fill(map, -1);
        var next = 0;

        for (var i = 0; i < n; i++)
        {
            var c = comm[i];
            if (map[c] == -1)
            {
                map[c] = next++;
            }

            comm[i] = map[c];
        }

        return next;
    }

    private static int LeidenRefine(ref LgGraph g, int[] comm, double gamma, double twom, int[] refined)
    {
        var n = g.N;
        var stot = new double[n];
        var acc = new double[n];
        var rsize = new int[n];
        var dirty = new int[n];

        for (var i = 0; i < n; i++)
        {
            refined[i] = i;
            stot[i] = g.K[i];
            rsize[i] = 1;
        }

        for (var v = 0; v < n; v++)
        {
            if (rsize[refined[v]] != 1)
            {
                continue;
            }

            var cv = comm[v];
            var ndirty = 0;

            for (var e = g.Off[v]; e < g.Off[v + 1]; e++)
            {
                var u = g.Nbr[e];
                if (u == v || comm[u] != cv)
                {
                    continue;
                }

                var ru = refined[u];
                if (acc[ru] == 0.0)
                {
                    dirty[ndirty++] = ru;
                }

                acc[ru] += g.W[e];
            }

            var rv = refined[v];
            var kv = g.K[v];
            stot[rv] -= kv;
            var bestR = rv;
            var bestGain = 0.0;

            for (var d = 0; d < ndirty; d++)
            {
                var r = dirty[d];
                if (r == rv)
                {
                    continue;
                }

                var gain = acc[r] - gamma * kv * stot[r] / twom;
                if (gain > bestGain)
                {
                    bestGain = gain;
                    bestR = r;
                }
            }

            if (bestR != rv)
            {
                refined[v] = bestR;
                stot[bestR] += kv;
                rsize[bestR]++;
                rsize[rv]--;
            }
            else
            {
                stot[rv] += kv;
            }

            for (var d = 0; d < ndirty; d++)
            {
                acc[dirty[d]] = 0.0;
            }
        }

        return LeidenRelabel(refined, n);
    }

    private static bool LeidenAggregate(
        ref LgGraph g,
        int[] refined,
        int rCount,
        int[] comm,
        out LgGraph outGraph,
        out int[] seed)
    {
        var n = g.N;
        var k2 = new double[rCount];
        var gcount = new int[rCount];
        var gstart = new int[rCount + 1];
        var members = new int[n];
        var fill = new int[rCount];
        var acc = new double[rCount];
        var dirty = new int[rCount];
        seed = new int[rCount];
        Array.Fill(seed, -1);

        for (var i = 0; i < n; i++)
        {
            var r = refined[i];
            k2[r] += g.K[i];
            gcount[r]++;
            if (seed[r] == -1)
            {
                seed[r] = comm[i];
            }
        }

        gstart[0] = 0;
        for (var r = 0; r < rCount; r++)
        {
            gstart[r + 1] = gstart[r] + gcount[r];
            fill[r] = gstart[r];
        }

        for (var i = 0; i < n; i++)
        {
            members[fill[refined[i]]++] = i;
        }

        var off2 = new int[rCount + 1];
        off2[0] = 0;

        for (var r = 0; r < rCount; r++)
        {
            var nd = 0;
            for (var m = gstart[r]; m < gstart[r + 1]; m++)
            {
                var i = members[m];
                for (var e = g.Off[i]; e < g.Off[i + 1]; e++)
                {
                    var rb = refined[g.Nbr[e]];
                    if (rb == r || acc[rb] != 0.0)
                    {
                        continue;
                    }

                    acc[rb] = 1.0;
                    dirty[nd++] = rb;
                }
            }

            off2[r + 1] = off2[r] + nd;
            for (var d = 0; d < nd; d++)
            {
                acc[dirty[d]] = 0.0;
            }
        }

        var total = off2[rCount];
        var nbr2 = new int[Math.Max(total, 1)];
        var w2 = new double[Math.Max(total, 1)];

        for (var r = 0; r < rCount; r++)
        {
            var nd = 0;
            for (var m = gstart[r]; m < gstart[r + 1]; m++)
            {
                var i = members[m];
                for (var e = g.Off[i]; e < g.Off[i + 1]; e++)
                {
                    var rb = refined[g.Nbr[e]];
                    if (rb == r)
                    {
                        continue;
                    }

                    if (acc[rb] == 0.0)
                    {
                        dirty[nd++] = rb;
                    }

                    acc[rb] += g.W[e];
                }
            }

            var b = off2[r];
            for (var d = 0; d < nd; d++)
            {
                nbr2[b + d] = dirty[d];
                w2[b + d] = acc[dirty[d]];
                acc[dirty[d]] = 0.0;
            }
        }

        outGraph = new LgGraph
        {
            N = rCount,
            Off = off2,
            Nbr = nbr2,
            W = w2,
            K = k2,
        };
        return true;
    }
}
