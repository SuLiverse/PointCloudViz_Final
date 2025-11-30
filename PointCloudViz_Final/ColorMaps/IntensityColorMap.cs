using System.Windows.Media;
using PointCloudViz_Final.Models;

namespace PointCloudViz_Final.Rendering
{
    public class IntensityColorMap : ColorMapBase
    {
        public override string Name => "Intensity";
        public override Color Map(PointRecord p, BoundingBox bbox)
        {
            byte v = (byte)System.Math.Clamp((int)(p.Intensity * 255f), 0, 255);
            return Color.FromRgb(v, v, v);
        }
    }
}
