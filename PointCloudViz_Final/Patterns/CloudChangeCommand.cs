using PointCloudViz_Final.Models;

namespace PointCloudViz_Final.Patterns;

public sealed class CloudChangeCommand(string description, PointCloud before, PointCloud after,
    Action<PointCloud> apply) : ICommand
{
    public string Description => description;
    public long RetainedBytes => ((long)before.Count + after.Count) *
        System.Runtime.CompilerServices.Unsafe.SizeOf<PointRecord>();
    public void Execute() => apply(after);
    public void Undo() => apply(before);
}
