using System.Numerics;
using PointCloudViz.Core.Data;

namespace PointCloudViz.Core.Spatial;

/// <summary>
/// 静态三维 KD 树（隐式平衡树，节点存放在排列后的索引数组中，无额外节点对象）。
/// <para>构建 O(n log n)，用于半径搜索、k 近邻和最近点查询，是离群点滤波与拾取的基础。</para>
/// </summary>
public sealed class KdTree
{
    private const int LeafSize = 12;
    private const int ParallelThreshold = 1 << 16;

    private readonly Vector3[] _positions;
    private readonly int[] _index;
    private readonly byte[] _axis;

    public KdTree(ReadOnlySpan<PointRecord> points)
    {
        _positions = new Vector3[points.Length];
        for (int i = 0; i < points.Length; i++) _positions[i] = points[i].Position;
        _index = new int[points.Length];
        for (int i = 0; i < _index.Length; i++) _index[i] = i;
        _axis = new byte[points.Length];
        Build(0, _index.Length, 0);
    }

    public KdTree(ReadOnlySpan<Vector3> positions)
    {
        _positions = positions.ToArray();
        _index = new int[positions.Length];
        for (int i = 0; i < _index.Length; i++) _index[i] = i;
        _axis = new byte[positions.Length];
        Build(0, _index.Length, 0);
    }

    public int Count => _positions.Length;

    public Vector3 Position(int index) => _positions[index];

    /// <summary>返回距离 <paramref name="center"/> 不超过 <paramref name="radius"/> 的点的索引。</summary>
    public void RadiusSearch(Vector3 center, float radius, List<int> results)
    {
        results.Clear();
        if (_index.Length == 0) return;
        RadiusSearch(0, _index.Length, center, radius, radius * radius, results);
    }

    /// <summary>
    /// 统计半径内的点数（含查询点自身，若它在树中）。达到 <paramref name="stopAt"/> 即提前返回，
    /// 半径离群点滤波只需要知道"是否够数"，提前终止可以显著加速。
    /// </summary>
    public int CountWithin(Vector3 center, float radius, int stopAt = int.MaxValue)
    {
        if (_index.Length == 0) return 0;
        int count = 0;
        CountWithin(0, _index.Length, center, radius, radius * radius, stopAt, ref count);
        return count;
    }

    /// <summary>
    /// k 近邻查询，结果按距离升序写入 <paramref name="indices"/> 与 <paramref name="distancesSquared"/>，
    /// 返回实际找到的数量（点数不足 k 时小于 k）。
    /// </summary>
    public int KNearest(Vector3 center, Span<int> indices, Span<float> distancesSquared)
    {
        int k = Math.Min(indices.Length, distancesSquared.Length);
        if (k == 0 || _index.Length == 0) return 0;
        var heap = new MaxHeap(indices[..k], distancesSquared[..k]);
        KNearest(0, _index.Length, center, ref heap);
        return heap.SortAscending();
    }

    /// <summary>最近点索引；树为空时返回 -1。</summary>
    public int Nearest(Vector3 center, out float distanceSquared)
    {
        Span<int> idx = stackalloc int[1];
        Span<float> dist = stackalloc float[1];
        int n = KNearest(center, idx, dist);
        distanceSquared = n > 0 ? dist[0] : float.PositiveInfinity;
        return n > 0 ? idx[0] : -1;
    }

    private void Build(int lo, int hi, int depth)
    {
        if (hi - lo <= LeafSize) return;

        int axis = LongestAxis(lo, hi);
        int mid = (lo + hi) >>> 1;
        Select(lo, hi - 1, mid, axis);
        _axis[mid] = (byte)axis;

        if (hi - lo > ParallelThreshold && depth < 6)
        {
            Parallel.Invoke(
                () => Build(lo, mid, depth + 1),
                () => Build(mid + 1, hi, depth + 1));
        }
        else
        {
            Build(lo, mid, depth + 1);
            Build(mid + 1, hi, depth + 1);
        }
    }

    private int LongestAxis(int lo, int hi)
    {
        var min = _positions[_index[lo]];
        var max = min;
        // 大区间抽样估计范围即可
        int stride = Math.Max(1, (hi - lo) / 1024);
        for (int i = lo; i < hi; i += stride)
        {
            var p = _positions[_index[i]];
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }
        var size = max - min;
        return size.X >= size.Y && size.X >= size.Z ? 0 : size.Y >= size.Z ? 1 : 2;
    }

    /// <summary>Hoare 划分的快速选择（nth_element），对大量重复坐标也能保持均衡。</summary>
    private void Select(int lo, int hi, int k, int axis)
    {
        var idx = _index;
        while (hi > lo)
        {
            float a = Coord(idx[lo], axis), b = Coord(idx[(lo + hi) >>> 1], axis), c = Coord(idx[hi], axis);
            float pivot = Math.Max(Math.Min(a, b), Math.Min(Math.Max(a, b), c));
            int i = lo, j = hi;
            while (i <= j)
            {
                while (Coord(idx[i], axis) < pivot) i++;
                while (Coord(idx[j], axis) > pivot) j--;
                if (i <= j)
                {
                    (idx[i], idx[j]) = (idx[j], idx[i]);
                    i++;
                    j--;
                }
            }
            if (k <= j) hi = j;
            else if (k >= i) lo = i;
            else return;
        }
    }

    private float Coord(int pointIndex, int axis) => Component(_positions[pointIndex], axis);

    private static float Component(Vector3 v, int axis) => axis switch { 0 => v.X, 1 => v.Y, _ => v.Z };

    private void RadiusSearch(int lo, int hi, Vector3 q, float r, float r2, List<int> results)
    {
        while (true)
        {
            if (hi - lo <= LeafSize)
            {
                for (int i = lo; i < hi; i++)
                {
                    int pi = _index[i];
                    if (Vector3.DistanceSquared(_positions[pi], q) <= r2) results.Add(pi);
                }
                return;
            }

            int mid = (lo + hi) >>> 1;
            int node = _index[mid];
            var p = _positions[node];
            if (Vector3.DistanceSquared(p, q) <= r2) results.Add(node);

            float diff = Component(q, _axis[mid]) - Component(p, _axis[mid]);
            bool goLeft = diff <= r, goRight = diff >= -r;
            if (goLeft && goRight)
            {
                RadiusSearch(lo, mid, q, r, r2, results);
                lo = mid + 1;
            }
            else if (goLeft) hi = mid;
            else if (goRight) lo = mid + 1;
            else return;
        }
    }

    private void CountWithin(int lo, int hi, Vector3 q, float r, float r2, int stopAt, ref int count)
    {
        while (count < stopAt)
        {
            if (hi - lo <= LeafSize)
            {
                for (int i = lo; i < hi && count < stopAt; i++)
                {
                    if (Vector3.DistanceSquared(_positions[_index[i]], q) <= r2) count++;
                }
                return;
            }

            int mid = (lo + hi) >>> 1;
            var p = _positions[_index[mid]];
            if (Vector3.DistanceSquared(p, q) <= r2) count++;

            float diff = Component(q, _axis[mid]) - Component(p, _axis[mid]);
            bool goLeft = diff <= r, goRight = diff >= -r;
            if (goLeft && goRight)
            {
                CountWithin(lo, mid, q, r, r2, stopAt, ref count);
                lo = mid + 1;
            }
            else if (goLeft) hi = mid;
            else if (goRight) lo = mid + 1;
            else return;
        }
    }

    private void KNearest(int lo, int hi, Vector3 q, ref MaxHeap heap)
    {
        if (hi - lo <= LeafSize)
        {
            for (int i = lo; i < hi; i++)
            {
                int pi = _index[i];
                heap.Offer(pi, Vector3.DistanceSquared(_positions[pi], q));
            }
            return;
        }

        int mid = (lo + hi) >>> 1;
        int node = _index[mid];
        var p = _positions[node];
        heap.Offer(node, Vector3.DistanceSquared(p, q));

        float diff = Component(q, _axis[mid]) - Component(p, _axis[mid]);
        // 先搜索查询点所在一侧，再视需要回溯另一侧
        if (diff <= 0)
        {
            KNearest(lo, mid, q, ref heap);
            if (!heap.IsFull || diff * diff <= heap.Worst) KNearest(mid + 1, hi, q, ref heap);
        }
        else
        {
            KNearest(mid + 1, hi, q, ref heap);
            if (!heap.IsFull || diff * diff <= heap.Worst) KNearest(lo, mid, q, ref heap);
        }
    }

    /// <summary>容量固定的最大堆，保存当前 k 个最近候选。</summary>
    private ref struct MaxHeap(Span<int> indices, Span<float> distances)
    {
        private readonly Span<int> _indices = indices;
        private readonly Span<float> _distances = distances;
        private int _count;

        public readonly bool IsFull => _count == _indices.Length;

        public readonly float Worst => _count == 0 ? float.PositiveInfinity : _distances[0];

        public void Offer(int index, float distance)
        {
            if (_count < _indices.Length)
            {
                int i = _count++;
                _indices[i] = index;
                _distances[i] = distance;
                SiftUp(i);
            }
            else if (distance < _distances[0])
            {
                _indices[0] = index;
                _distances[0] = distance;
                SiftDown(0, _count);
            }
        }

        /// <summary>原地堆排序为升序，返回元素个数。</summary>
        public int SortAscending()
        {
            for (int end = _count - 1; end > 0; end--)
            {
                Swap(0, end);
                SiftDown(0, end);
            }
            return _count;
        }

        private readonly void SiftUp(int i)
        {
            while (i > 0)
            {
                int parent = (i - 1) >> 1;
                if (_distances[parent] >= _distances[i]) break;
                Swap(i, parent);
                i = parent;
            }
        }

        private readonly void SiftDown(int i, int size)
        {
            while (true)
            {
                int l = 2 * i + 1, r = l + 1, largest = i;
                if (l < size && _distances[l] > _distances[largest]) largest = l;
                if (r < size && _distances[r] > _distances[largest]) largest = r;
                if (largest == i) return;
                Swap(i, largest);
                i = largest;
            }
        }

        private readonly void Swap(int a, int b)
        {
            (_indices[a], _indices[b]) = (_indices[b], _indices[a]);
            (_distances[a], _distances[b]) = (_distances[b], _distances[a]);
        }
    }
}
