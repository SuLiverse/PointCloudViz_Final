using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PointCloudViz_Final.Models;

namespace PointCloudViz_Final.Rendering
{
    public interface IRenderer
    {
        Task<WriteableBitmap> RenderAsync(
            PointCloud cloud,
            PointCloudViz_Final.Models.Camera camera,
            int width, int height,
            IColorMap colorMap,
            int pointSize,
            Color background,
            CancellationToken token,
            bool isInteracting = false);
    }
}
