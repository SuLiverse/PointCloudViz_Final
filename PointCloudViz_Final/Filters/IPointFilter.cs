using System.Collections.Generic;
using PointCloudViz_Final.Models;

namespace PointCloudViz_Final.Filters
{
    public interface IPointFilter
    {
        IEnumerable<PointRecord> Apply(IEnumerable<PointRecord> input, BoundingBox bbox);
        string Name { get; }
    }
}
