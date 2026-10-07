using PointCloudViz_Final.Models;

namespace PointCloudViz_Final.Services;

public sealed record CloudStatistics(int Count, double MeanZ, double StandardDeviationZ, int[] HeightBins)
{
    public static CloudStatistics Calculate(PointCloud cloud, int binCount = 32)
    {
        if (binCount < 1) throw new ArgumentOutOfRangeException(nameof(binCount));
        int count = 0;
        double mean = 0, m2 = 0;
        var bins = new int[binCount];
        double range = (double)cloud.BBox.MaxZ - cloud.BBox.MinZ;
        foreach (var p in cloud.Points)
        {
            count++;
            double delta = p.Z - mean;
            mean += delta / count;
            m2 += delta * (p.Z - mean);
            int bin = range == 0 ? binCount / 2 : Math.Clamp((int)((p.Z - cloud.BBox.MinZ) / range * binCount), 0, binCount - 1);
            bins[bin]++;
        }
        return new(count, mean, count == 0 ? 0 : Math.Sqrt(m2 / count), bins);
    }
}
