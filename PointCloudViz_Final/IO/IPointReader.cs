using System.Threading;
using System.Threading.Tasks;
using PointCloudViz_Final.Models;

namespace PointCloudViz_Final.IO
{
    public interface IPointReader
    {
        bool CanRead(string extension);
        Task<PointCloud> ReadAsync(string path, CancellationToken token);
        string Name { get; }
    }
}
