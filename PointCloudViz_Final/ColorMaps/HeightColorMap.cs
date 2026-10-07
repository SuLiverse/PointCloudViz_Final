using System.Windows.Media;
using PointCloudViz_Final.Models;

namespace PointCloudViz_Final.Rendering;

public class HeightColorMap : ColorMapBase
{
    private static readonly (float Position, Color Color)[] Stops =
    [
        (0, Color.FromRgb(40, 94, 145)), (.3f, Color.FromRgb(52, 182, 177)),
        (.55f, Color.FromRgb(144, 209, 154)), (.8f, Color.FromRgb(242, 214, 110)),
        (1, Color.FromRgb(227, 130, 104))
    ];
    public override string Name => "Height";
    public override Color Map(PointRecord p, BoundingBox bbox)
    {
        double range = (double)bbox.MaxZ - bbox.MinZ;
        float t = range < 1e-9 ? .5f : (float)Math.Clamp(((double)p.Z - bbox.MinZ) / range, 0, 1);
        for (int i = 1; i < Stops.Length; i++)
        {
            if (t > Stops[i].Position) continue;
            var a = Stops[i - 1]; var b = Stops[i];
            float blend = (t - a.Position) / (b.Position - a.Position);
            return Color.FromRgb((byte)(a.Color.R + (b.Color.R - a.Color.R) * blend),
                (byte)(a.Color.G + (b.Color.G - a.Color.G) * blend),
                (byte)(a.Color.B + (b.Color.B - a.Color.B) * blend));
        }
        return Stops[^1].Color;
    }
}
