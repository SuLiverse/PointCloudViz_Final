namespace PointCloudViz_Final.Patterns
{
    /// <summary>命令模式：命令接口</summary>
    public interface ICommand
    {
        void Execute();
        void Undo();
        string Description { get; }
    }
}

