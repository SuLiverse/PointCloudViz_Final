using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PointCloudViz_Final.Models;

namespace PointCloudViz_Final.Rendering
{
    /// <summary>支持LOD开关的渲染器基类</summary>
    public abstract class LodAwareRendererBase : OptimizedRendererBase
    {
        private static bool _globalLodEnabled = true;

        public static bool GlobalLodEnabled
        {
            get => _globalLodEnabled;
            set => _globalLodEnabled = value;
        }

        protected override bool IsLodEnabled() => _globalLodEnabled;
    }
}

