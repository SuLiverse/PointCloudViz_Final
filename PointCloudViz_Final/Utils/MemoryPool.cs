using System;
using System.Buffers;
using System.Collections.Generic;

namespace PointCloudViz_Final.Utils
{
    /// <summary>内存池：减少GC压力，复用大数组</summary>
    public static class MemoryPool
    {
        private static readonly Dictionary<int, Queue<byte[]>> _pools = new();
        private static readonly object _lock = new object();

        /// <summary>从池中获取或创建数组</summary>
        public static byte[] Rent(int minLength)
        {
            // 向上取整到最近的2的幂，便于池化
            int size = RoundUpToPowerOfTwo(minLength);

            lock (_lock)
            {
                if (_pools.TryGetValue(size, out var queue) && queue.Count > 0)
                {
                    return queue.Dequeue();
                }
            }

            // 池中没有，创建新的
            return new byte[size];
        }

        /// <summary>归还数组到池</summary>
        public static void Return(byte[] array)
        {
            if (array == null) return;

            int size = array.Length;
            if (size < 1024) return; // 太小的数组不池化

            lock (_lock)
            {
                if (!_pools.ContainsKey(size))
                    _pools[size] = new Queue<byte[]>();

                var queue = _pools[size];
                if (queue.Count < 10) // 限制池大小
                {
                    queue.Enqueue(array);
                }
            }
        }

        /// <summary>使用ArrayPool（.NET标准库）</summary>
        public static ArrayPool<byte> SharedArrayPool => ArrayPool<byte>.Shared;

        private static int RoundUpToPowerOfTwo(int value)
        {
            if (value <= 0) return 1;
            value--;
            value |= value >> 1;
            value |= value >> 2;
            value |= value >> 4;
            value |= value >> 8;
            value |= value >> 16;
            return value + 1;
        }

        /// <summary>清理池（释放内存）</summary>
        public static void Clear()
        {
            lock (_lock)
            {
                _pools.Clear();
            }
        }
    }
}

