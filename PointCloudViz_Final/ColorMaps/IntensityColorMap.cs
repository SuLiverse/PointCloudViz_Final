using System.Windows.Media;
using PointCloudViz_Final.Models;

namespace PointCloudViz_Final.Rendering
{
    public class IntensityColorMap : ColorMapBase
    {
        public override string Name => "Intensity";
        public override Color Map(PointRecord p, BoundingBox bbox)
        {
            byte v = (byte)(System.Math.Clamp(p.Intensity, 0f, 1f) * 255f);
            return Color.FromRgb(v, v, v);
        }
    }
}
