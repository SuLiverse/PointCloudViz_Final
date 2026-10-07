using PointCloudViz_Final.Filters;
using PointCloudViz_Final.Models;
using PointCloudViz_Final.Patterns;
using PointCloudViz_Final.Rendering;
using PointCloudViz_Final.Services;
using PointCloudViz_Final.Tools;
using System.Numerics;
using System.Windows;
using System.Windows.Media;
using Xunit;

namespace PointCloudViz_Final.Tests;

public class CoreTests
{
    [Fact]
    public void DemoIsDeterministicAndCancellable()
    {
        var first = DemoSceneFactory.Create(42);
        var second = DemoSceneFactory.Create(42);
        Assert.True(first.Count > 100_000);
        Assert.Equal(first.Points[1234], second.Points[1234]);
        Assert.Throws<OperationCanceledException>(() => DemoSceneFactory.Create(42, new CancellationToken(true)));
    }
    [Fact]
    public void ModelNeverSilentlySamples()
    {
        var cloud = new PointCloud(Enumerable.Range(0, 1_200_001).Select(i => new PointRecord(i, 0, 0)));
        Assert.Equal(1_200_001, cloud.Count);
        cloud.Replace(cloud.Points);
        Assert.Equal(1_200_001, cloud.Count);
    }

    [Fact]
    public void ModelReplacementIsTransactional()
    {
        var cloud = new PointCloud([new(1, 2, 3)]);
        Assert.Throws<ArgumentException>(() => cloud.Replace([new(float.NaN, 0, 0)]));
        Assert.Equal(1, cloud.Count);
        Assert.Equal(3, cloud.BBox.MaxZ);
    }

    [Fact]
    public void VoxelCoordinatesDoNotCollide()
    {
        // The old polynomial hash merged (0, 1, 0) and (0, 0, 31).
        PointRecord[] points = [new(0, 1, 0), new(0, 0, 31)];
        var result = new VoxelGridFilter(1).Apply(points, BoundingBox.FromPoints(points)).ToArray();
        Assert.Equal(2, result.Length);
    }

    [Fact]
    public void VoxelPreservesCentroidAndAttributes()
    {
        PointRecord[] points = [new(0, 0, 0, .2f, Colors.Red), new(.5f, .5f, .5f, .8f, Colors.Blue)];
        var point = Assert.Single(new VoxelGridFilter(1).Apply(points, BoundingBox.FromPoints(points)));
        Assert.Equal(.25f, point.X);
        Assert.Equal(.5f, point.Intensity, 5);
        Assert.InRange(point.Color.R, (byte)127, (byte)128);
        Assert.InRange(point.Color.B, (byte)127, (byte)128);
    }

    [Theory]
    [InlineData(0)] [InlineData(-1)] [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)]
    public void FiltersRejectInvalidSizes(float value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new VoxelGridFilter(value));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RadiusOutlierFilter(value, 1));
    }

    [Fact]
    public void RangeAndNeighborCountsAreValidated()
    {
        Assert.Throws<ArgumentException>(() => new RangeFilter(4, 1));
        Assert.Throws<ArgumentException>(() => new RangeFilter(float.NaN, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RadiusOutlierFilter(1, 0));
    }

    [Fact]
    public void OutlierFilterExcludesSelfAndKeepsDenseCluster()
    {
        PointRecord[] points = [new(0, 0, 0), new(.1f, 0, 0), new(.2f, 0, 0), new(99, 99, 99)];
        var filtered = new RadiusOutlierFilter(.3f, 2).Apply(points, BoundingBox.FromPoints(points)).ToList();
        Assert.Equal(3, filtered.Count);
        Assert.DoesNotContain(filtered, p => p.X == 99);
    }

    [Fact]
    public void FiltersHonorCancellation()
    {
        var token = new CancellationToken(true);
        PointRecord[] points = [new(1, 2, 3)];
        foreach (var filter in new IPointFilter[] { new VoxelGridFilter(1), new RangeFilter(0, 4), new RadiusOutlierFilter(1, 1) })
            Assert.Throws<OperationCanceledException>(() => filter.Apply(points, default, token).ToList());
    }

    [Fact]
    public void EmptyCloudHasFiniteStatistics()
    {
        var cloud = new PointCloud([]);
        Assert.Equal((0, 0f, 0f, 0f), cloud.StatsZ());
        Assert.Equal(0, CloudStatistics.Calculate(cloud).StandardDeviationZ);
    }

    [Fact]
    public void StatisticsUseStableAccumulators()
    {
        var cloud = new PointCloud([new(0, 0, 1), new(0, 0, 2), new(0, 0, 3)]);
        var stats = CloudStatistics.Calculate(cloud);
        Assert.Equal(2, stats.MeanZ);
        Assert.Equal(Math.Sqrt(2.0 / 3), stats.StandardDeviationZ, 10);
        Assert.Equal(3, stats.HeightBins.Sum());
    }

    [Theory]
    [InlineData(0, 1)] [InlineData(1_000_000, 1)] [InlineData(1_000_001, 2)]
    [InlineData(1_999_999, 2)] [InlineData(int.MaxValue, 2148)]
    public void RenderBudgetIsCeilingBased(int count, int expected)
    {
        int step = RenderSampling.Step(count, 1_000_000);
        Assert.Equal(expected, step);
        Assert.True(((long)count + step - 1) / step <= 1_000_000);
    }

    [Fact]
    public void HistoryEvictsOldestWithoutReversingOrder()
    {
        var manager = new CommandManager();
        int state = 0;
        for (int i = 1; i <= 60; i++)
        {
            int next = i, previous = i - 1;
            manager.Execute(new DelegateCommand(() => state = next, () => state = previous));
        }
        for (int i = 59; i >= 10; i--) { manager.Undo(); Assert.Equal(i, state); }
        Assert.False(manager.CanUndo);
        for (int i = 11; i <= 60; i++) { manager.Redo(); Assert.Equal(i, state); }
        Assert.False(manager.CanRedo);
        manager.Undo();
        manager.Execute(new DelegateCommand(() => state = 100, () => state = 59));
        Assert.False(manager.CanRedo);
    }

    [Fact]
    public void FailedUndoDoesNotConsumeHistory()
    {
        var manager = new CommandManager();
        manager.Execute(new DelegateCommand(() => { }, () => throw new InvalidOperationException()));
        Assert.Throws<InvalidOperationException>(() => manager.Undo());
        Assert.True(manager.CanUndo);
        Assert.False(manager.CanRedo);
    }

    [Fact]
    public void EveryCloudChangeCanUndoIncludingEmptyResults()
    {
        var original = new PointCloud([new(1, 2, 3)]);
        var empty = new PointCloud([]);
        var current = original;
        var manager = new CommandManager();
        manager.Execute(new CloudChangeCommand("range", original, empty, c => current = c));
        Assert.Empty(current.Points);
        manager.Undo();
        Assert.Same(original, current);
        manager.Redo();
        Assert.Same(empty, current);
        manager.Clear();
        Assert.False(manager.CanUndo);
    }

    [Fact]
    public void PickingAcceptsOriginAndRejectsBackgroundAndBehindCamera()
    {
        var matrix = Matrix4x4.CreateLookAt(new Vector3(0, 0, 10), Vector3.Zero, Vector3.UnitY)
            * Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 2, 1, .1f, 100);
        PointRecord[] points = [new(0, 0, 0), new(2, 2, 0), new(0, 0, 20)];
        Assert.Equal(Vector3.Zero, MeasurementTool.PickPoint(new Point(100, 100), points, matrix, 200, 200));
        Assert.Null(MeasurementTool.PickPoint(new Point(2, 2), points, matrix, 200, 200));
        Assert.Null(MeasurementTool.PickPoint(new Point(100, 100), [new(0, 0, 20)], matrix, 200, 200));
    }

    [Fact]
    public void AreaWorksOnVerticalPlaneWithoutNan()
    {
        Assert.True(MeasurementTool.TryArea([new(0, 0, 0), new(2, 0, 0), new(2, 0, 3), new(0, 0, 3)], out var area));
        Assert.Equal(6, area, 5);
    }

    [Fact]
    public void AreaPreservesConcaveBoundaryAndRejectsInvalidPolygons()
    {
        Assert.True(MeasurementTool.TryArea([new(0, 0, 0), new(2, 0, 0), new(1, 1, 0), new(2, 2, 0), new(0, 2, 0)], out var area));
        Assert.Equal(3, area, 5);
        Assert.False(MeasurementTool.TryArea([new(0, 0, 0), new(2, 2, 0), new(0, 2, 0), new(2, 0, 0)], out _));
        Assert.False(MeasurementTool.TryArea([new(0, 0, 0), new(1, 0, 0), new(2, 0, 0)], out _));
        Assert.False(MeasurementTool.TryArea([new(0, 0, 0), new(2, 0, 0), new(2, 2, 0), new(0, 2, 1)], out _));
    }

    [Fact]
    public void NewAreaDoesNotReplacePreviousFinishedMeasurement()
    {
        var tool = new MeasurementTool { Mode = MeasurementMode.Area, IsActive = true };
        tool.AddPoint(new(0, 0, 0)); tool.AddPoint(new(1, 0, 0)); tool.AddPoint(new(0, 1, 0));
        tool.ClearSelection();
        tool.AddPoint(new(0, 0, 1)); tool.AddPoint(new(1, 0, 1)); tool.AddPoint(new(0, 1, 1));
        Assert.Equal(2, tool.Measurements.Count);
    }

    private sealed class DelegateCommand(Action execute, Action undo) : Patterns.ICommand
    {
        public string Description => "test";
        public void Execute() => execute();
        public void Undo() => undo();
    }

    [Fact]
    public void IntensityMappingClampsBeforeIntegerConversion()
    {
        Assert.Equal(Colors.White, new IntensityColorMap().Map(new(0, 0, 0, float.MaxValue), default));
        Assert.Equal(Colors.Black, new IntensityColorMap().Map(new(0, 0, 0, -1), default));
    }

    [Fact]
    public void MemoryBudgetEvictsOldHistoryButKeepsNewestUndo()
    {
        var manager = new CommandManager();
        for (int i = 0; i < 10; i++) manager.Execute(new HeavyCommand());
        int remaining = 0;
        while (manager.CanUndo) { manager.Undo(); remaining++; }
        Assert.Equal(2, remaining);
    }

    private sealed class HeavyCommand : Patterns.ICommand
    {
        public string Description => "heavy";
        public long RetainedBytes => 100L * 1024 * 1024;
        public void Execute() { }
        public void Undo() { }
    }
}
