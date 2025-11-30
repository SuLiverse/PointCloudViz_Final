using System;
using System.Collections.Generic;
using System.Linq;

namespace PointCloudViz_Final.Utils
{
    /// <summary>LRU缓存：按需加载，超限淘汰</summary>
    public class LruCache<TKey, TValue> where TValue : class
    {
        private readonly Dictionary<TKey, CacheItem> _cache = new();
        private readonly int _maxSize;
        private readonly object _lock = new object();

        public int Count
        {
            get
            {
                lock (_lock) return _cache.Count;
            }
        }

        public LruCache(int maxSize)
        {
            _maxSize = maxSize;
        }

        /// <summary>获取值</summary>
        public TValue? Get(TKey key)
        {
            lock (_lock)
            {
                if (_cache.TryGetValue(key, out var item))
                {
                    item.LastAccessTime = DateTime.Now;
                    return item.Value;
                }
                return null;
            }
        }

        /// <summary>添加或更新值</summary>
        public void Put(TKey key, TValue value)
        {
            lock (_lock)
            {
                if (_cache.ContainsKey(key))
                {
                    _cache[key].Value = value;
                    _cache[key].LastAccessTime = DateTime.Now;
                }
                else
                {
                    // 检查是否需要淘汰
                    if (_cache.Count >= _maxSize)
                    {
                        EvictLeastRecentlyUsed();
                    }

                    _cache[key] = new CacheItem { Value = value, LastAccessTime = DateTime.Now };
                }
            }
        }

        /// <summary>移除值</summary>
        public bool Remove(TKey key)
        {
            lock (_lock)
            {
                return _cache.Remove(key);
            }
        }

        /// <summary>清除所有</summary>
        public void Clear()
        {
            lock (_lock)
            {
                _cache.Clear();
            }
        }

        /// <summary>淘汰最近最少使用的项</summary>
        private void EvictLeastRecentlyUsed()
        {
            if (_cache.Count == 0) return;

            var lru = _cache.OrderBy(kvp => kvp.Value.LastAccessTime).First();
            _cache.Remove(lru.Key);
        }

        private class CacheItem
        {
            public TValue Value { get; set; } = null!;
            public DateTime LastAccessTime { get; set; }
        }
    }
}

