using System.Windows.Media;
using PointCloudViz_Final.Models;

namespace PointCloudViz_Final.Rendering;

public sealed class OriginalColorMap : ColorMapBase
{
    public override string Name => "RGB";
    public override Color Map(PointRecord p, BoundingBox bbox) => p.Color;
}
