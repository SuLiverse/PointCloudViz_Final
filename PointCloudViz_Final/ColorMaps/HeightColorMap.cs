using System.Windows.Media;
using PointCloudViz_Final.Models;

namespace PointCloudViz_Final.Rendering
{
    public class HeightColorMap : ColorMapBase
    {
        public override string Name => "Height";
        public override Color Map(PointRecord p, BoundingBox bbox)
        {
            float t = (bbox.MaxZ - bbox.MinZ) < 1e-6f ? 0.5f : (p.Z - bbox.MinZ) / (bbox.MaxZ - bbox.MinZ);
            if (t < 0.25f) return Lerp(Colors.DarkBlue, Colors.Cyan, t / 0.25f);
            else if (t < 0.5f) return Lerp(Colors.Cyan, Colors.Green, (t - 0.25f) / 0.25f);
            else if (t < 0.75f) return Lerp(Colors.Green, Colors.Yellow, (t - 0.5f) / 0.25f);
            else return Lerp(Colors.Yellow, Colors.Red, (t - 0.75f) / 0.25f);
        }
        private static Color Lerp(Color a, Color b, float t)
            => Color.FromRgb((byte)(a.R + (b.R - a.R) * t),
                             (byte)(a.G + (b.G - a.G) * t),
                             (byte)(a.B + (b.B - a.B) * t));
    }
}
