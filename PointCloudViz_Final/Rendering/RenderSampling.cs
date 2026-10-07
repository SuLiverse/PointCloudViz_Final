namespace PointCloudViz_Final.Rendering;

public static class RenderSampling
{
    public static int Step(int pointCount, int budget)
    {
        if (pointCount < 0 || budget < 1) throw new ArgumentOutOfRangeException();
        return (int)Math.Max(1, ((long)pointCount + budget - 1) / budget);
    }
}
