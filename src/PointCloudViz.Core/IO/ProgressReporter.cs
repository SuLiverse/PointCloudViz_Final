namespace PointCloudViz.Core.IO;

/// <summary>节流的进度上报：只在进度变化超过 0.5% 时回调，避免刷爆 UI 线程。</summary>
internal sealed class ProgressReporter(IProgress<double>? progress)
{
    private double _last = -1;

    public void Report(double fraction)
    {
        if (progress is null) return;
        fraction = Math.Clamp(fraction, 0, 1);
        if (fraction - _last < 0.005 && fraction < 1) return;
        _last = fraction;
        progress.Report(fraction);
    }

    public void Report(long done, long total)
    {
        if (total > 0) Report((double)done / total);
    }
}
