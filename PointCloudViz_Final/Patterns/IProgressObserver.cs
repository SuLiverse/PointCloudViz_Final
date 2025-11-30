namespace PointCloudViz_Final.Patterns
{
    /// <summary>观察者模式：进度观察者接口</summary>
    public interface IProgressObserver
    {
        void OnProgressChanged(double percent, string? message = null);
    }
}

