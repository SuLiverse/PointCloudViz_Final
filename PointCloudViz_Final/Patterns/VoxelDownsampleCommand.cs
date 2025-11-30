using PointCloudViz_Final.Filters;
using PointCloudViz_Final.Models;

namespace PointCloudViz_Final.Patterns
{
    /// <summary>命令模式：体素下采样命令</summary>
    public class VoxelDownsampleCommand : ICommand
    {
        private readonly PointCloud _originalCloud;
        private PointCloud? _filteredCloud;
        private readonly float _voxelSize;
        private readonly System.Action<PointCloud> _onExecute;
        private readonly System.Action<PointCloud> _onUndo;

        public string Description => $"体素下采样 (大小: {_voxelSize:F2})";

        public VoxelDownsampleCommand(
            PointCloud originalCloud, 
            float voxelSize,
            System.Action<PointCloud> onExecute,
            System.Action<PointCloud> onUndo)
        {
            _originalCloud = originalCloud;
            _voxelSize = voxelSize;
            _onExecute = onExecute;
            _onUndo = onUndo;
        }

        public void Execute()
        {
            if (_filteredCloud == null)
            {
                var filter = new VoxelGridFilter(_voxelSize);
                var filtered = filter.Apply(_originalCloud.Points, _originalCloud.BBox);
                _filteredCloud = new PointCloud(filtered);
            }
            _onExecute(_filteredCloud);
        }

        public void Undo()
        {
            _onUndo(_originalCloud);
        }
    }
}

