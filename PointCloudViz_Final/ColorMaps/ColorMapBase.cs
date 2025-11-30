using System.Windows.Media;
using PointCloudViz_Final.Models;

namespace PointCloudViz_Final.Rendering
{
    public abstract class ColorMapBase : IColorMap
    {
        public virtual string Name => "Base";
        public abstract Color Map(PointRecord p, BoundingBox bbox);
    }
}
