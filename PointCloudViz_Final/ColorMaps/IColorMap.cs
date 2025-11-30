using System.Windows.Media;
using PointCloudViz_Final.Models;

namespace PointCloudViz_Final.Rendering
{
    public interface IColorMap
    {
        Color Map(PointRecord p, BoundingBox bbox);
        string Name { get; }
    }
}
