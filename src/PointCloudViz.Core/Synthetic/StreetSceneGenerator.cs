using PointCloudViz.Core.Data;

namespace PointCloudViz.Core.Synthetic;

/// <summary>合成街景参数（单位：米）。</summary>
public sealed record StreetSceneOptions
{
    public double RoadLength { get; init; } = 80;
    public double RoadWidth { get; init; } = 10;
    public double SidewalkWidth { get; init; } = 2.5;
    public double CurbHeight { get; init; } = 0.15;

    /// <summary>地面采样间距；越小点越密。</summary>
    public double Spacing { get; init; } = 0.08;

    public bool Buildings { get; init; } = true;
    public bool Trees { get; init; } = true;
    public bool Cars { get; init; } = true;
    public bool Poles { get; init; } = true;

    public int TreeCount { get; init; } = 8;
    public int CarCount { get; init; } = 6;
    public int PoleCount { get; init; } = 6;

    /// <summary>离群噪声点比例，用于演示离群点滤波。</summary>
    public double NoiseRatio { get; init; } = 0.003;

    public int Seed { get; init; } = 2025;

    /// <summary>
    /// 世界坐标偏移。默认模拟 UTM 投影坐标（百万量级），用来验证大坐标下的精度处理。
    /// </summary>
    public Double3 Offset { get; init; } = new(670_000, 4_860_000, 220);
}

/// <summary>
/// 合成街景点云生成器：道路（含标线）、路缘石、人行道、建筑立面（含窗户）、行道树、车辆、路灯和噪声点，
/// 并按 ASPRS 规范写入分类码，可直接用于演示分类着色与滤波。
/// <para>旧版逐点 <c>await WriteLineAsync</c> 写文本，新版在内存中生成后交给任意写出器，速度快一个数量级。</para>
/// </summary>
public static class StreetSceneGenerator
{
    /// <summary>自定义分类：车辆。</summary>
    public const byte ClassVehicle = 64;

    /// <summary>自定义分类：杆状物（路灯）。</summary>
    public const byte ClassPole = 65;

    private const byte ClassUnclassified = 1, ClassGround = 2, ClassHighVegetation = 5,
        ClassBuilding = 6, ClassLowNoise = 7, ClassRoad = 11, ClassHighNoise = 18;

    public static PointCloud Generate(StreetSceneOptions? options = null, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        var o = options ?? new StreetSceneOptions();
        if (o.Spacing <= 0) throw new ArgumentOutOfRangeException(nameof(options), "采样间距必须大于 0。");

        var ctx = new Context(o);
        var steps = new List<Action>
        {
            ctx.Road,
            ctx.Sidewalks,
        };
        if (o.Buildings) steps.Add(ctx.Buildings);
        if (o.Trees) steps.Add(ctx.Trees);
        if (o.Cars) steps.Add(ctx.Cars);
        if (o.Poles) steps.Add(ctx.Poles);
        steps.Add(ctx.Noise);

        for (int i = 0; i < steps.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            steps[i]();
            progress?.Report((i + 1) / (double)steps.Count);
        }
        return ctx.Builder.Build("synthetic_street");
    }

    private sealed class Context(StreetSceneOptions o)
    {
        private readonly Random _rng = new(o.Seed);

        public PointCloudBuilder Builder { get; } = CreateBuilder(o);

        private double HalfRoad => o.RoadWidth / 2;
        private double FacadeX => HalfRoad + o.SidewalkWidth;

        private static PointCloudBuilder CreateBuilder(StreetSceneOptions o)
        {
            var b = new PointCloudBuilder(1 << 16, o.Offset);
            b.AddAttributes(PointAttributes.Color | PointAttributes.Intensity | PointAttributes.Classification);
            return b;
        }

        private void Add(double x, double y, double z, Rgb24 color, byte cls, double reflectance)
        {
            // 强度模拟 16 位激光回波强度，带少量随机起伏
            double intensity = Math.Clamp(reflectance + Gaussian(0.03), 0, 1) * 40000;
            Builder.Add(x + o.Offset.X, y + o.Offset.Y, z + o.Offset.Z, (float)intensity, Jitter(color, 6), cls);
        }

        public void Road()
        {
            double s = o.Spacing;
            for (double y = 0; y <= o.RoadLength; y += s)
            for (double x = -HalfRoad; x <= HalfRoad; x += s)
            {
                double px = x + Uniform(s * 0.3), py = y + Uniform(s * 0.3);
                // 路拱：中间略高，便于排水
                double z = 0.06 * (1 - (px / HalfRoad) * (px / HalfRoad)) + Gaussian(0.004);
                bool centerLine = Math.Abs(px) < 0.12 && py % 6.0 < 3.0;
                bool edgeLine = Math.Abs(Math.Abs(px) - (HalfRoad - 0.4)) < 0.08;
                bool crossing = py > o.RoadLength * 0.45 && py < o.RoadLength * 0.45 + 4 && (px + HalfRoad) % 1.0 < 0.5;
                if (centerLine)
                    Add(px, py, z, new Rgb24(225, 190, 60), ClassRoad, 0.85);
                else if (edgeLine || crossing)
                    Add(px, py, z, new Rgb24(232, 232, 228), ClassRoad, 0.9);
                else
                    Add(px, py, z, new Rgb24(58, 60, 66), ClassRoad, 0.18);
            }
        }

        public void Sidewalks()
        {
            double s = o.Spacing;
            foreach (int side in new[] { -1, 1 })
            {
                for (double y = 0; y <= o.RoadLength; y += s)
                {
                    // 路缘石立面
                    for (double z = 0; z < o.CurbHeight; z += s * 0.5)
                        Add(side * HalfRoad, y + Uniform(s * 0.3), z, new Rgb24(150, 150, 146), ClassGround, 0.5);

                    for (double w = 0; w <= o.SidewalkWidth; w += s)
                    {
                        double px = side * (HalfRoad + w + Uniform(s * 0.3));
                        // 方砖缝
                        bool joint = (y % 0.6) < 0.04 || (w % 0.6) < 0.04;
                        var color = joint ? new Rgb24(120, 118, 112) : new Rgb24(176, 170, 160);
                        Add(px, y + Uniform(s * 0.3), o.CurbHeight + Gaussian(0.003), color, ClassGround, joint ? 0.35 : 0.55);
                    }
                }
            }
        }

        public void Buildings()
        {
            double s = o.Spacing * 1.5;
            Rgb24[] facadeColors = [new(196, 180, 160), new(170, 120, 100), new(210, 205, 195), new(150, 160, 170), new(185, 160, 120)];
            foreach (int side in new[] { -1, 1 })
            {
                double y0 = 0;
                while (y0 < o.RoadLength)
                {
                    double length = Math.Min(8 + _rng.NextDouble() * 14, o.RoadLength - y0);
                    double height = 6 + _rng.NextDouble() * 12;
                    double setback = _rng.NextDouble() * 0.8;
                    var wall = facadeColors[_rng.Next(facadeColors.Length)];
                    double x = side * (FacadeX + setback);

                    for (double y = y0; y < y0 + length; y += s)
                    for (double z = o.CurbHeight; z < height; z += s)
                    {
                        // 窗户：每层 3 m，窗宽 1.2 m、高 1.5 m，间隔 2.5 m
                        double floorZ = (z - o.CurbHeight) % 3.0, bayY = (y - y0) % 2.5;
                        bool window = z > 2.5 && floorZ > 1.0 && floorZ < 2.5 && bayY > 0.65 && bayY < 1.85;
                        double depth = window ? side * 0.15 : 0; // 窗户内凹
                        Add(x + depth + Gaussian(0.004), y + Uniform(s * 0.3), z + Uniform(s * 0.3),
                            window ? new Rgb24(70, 90, 110) : wall, ClassBuilding, window ? 0.1 : 0.6);
                    }

                    // 屋檐线
                    for (double y = y0; y < y0 + length; y += s)
                    for (double w = 0; w < 0.5; w += s)
                        Add(x + side * w, y, height, new Rgb24(90, 90, 95), ClassBuilding, 0.4);

                    y0 += length + 0.3;
                }
            }
        }

        public void Trees()
        {
            for (int i = 0; i < o.TreeCount; i++)
            {
                int side = i % 2 == 0 ? -1 : 1;
                double cx = side * (HalfRoad + 0.9);
                double cy = (i + 0.5) * o.RoadLength / o.TreeCount + Uniform(2);
                double trunkHeight = 2.2 + _rng.NextDouble() * 0.8;
                double crown = 1.6 + _rng.NextDouble() * 0.8;

                for (int k = 0; k < 900; k++)
                {
                    double a = _rng.NextDouble() * Math.Tau, z = _rng.NextDouble() * trunkHeight;
                    double r = 0.14 + 0.03 * (1 - z / trunkHeight);
                    Add(cx + r * Math.Cos(a), cy + r * Math.Sin(a), o.CurbHeight + z, new Rgb24(105, 75, 50), ClassHighVegetation, 0.3);
                }

                int leaves = (int)(2600 * crown * crown / 4);
                double czz = o.CurbHeight + trunkHeight + crown * 0.8;
                for (int k = 0; k < leaves; k++)
                {
                    // 椭球壳内随机分布，越靠外越密，模拟激光只打到树冠表面
                    var (dx, dy, dz) = RandomUnitVector();
                    double rr = crown * Math.Pow(_rng.NextDouble(), 0.25);
                    var green = new Rgb24((byte)(40 + _rng.Next(40)), (byte)(120 + _rng.Next(60)), (byte)(40 + _rng.Next(30)));
                    Add(cx + dx * rr, cy + dy * rr, czz + dz * rr * 0.8, green, ClassHighVegetation, 0.45);
                }
            }
        }

        public void Cars()
        {
            Rgb24[] paints = [new(190, 30, 35), new(230, 160, 20), new(30, 110, 200), new(235, 235, 235), new(40, 40, 45), new(120, 125, 130)];
            for (int i = 0; i < o.CarCount; i++)
            {
                int lane = i % 2 == 0 ? -1 : 1;
                double cx = lane * (HalfRoad / 2);
                double cy = (i + 0.5) * o.RoadLength / o.CarCount + Uniform(3);
                double length = 4.2 + _rng.NextDouble() * 0.6, width = 1.8, bodyH = 0.9, cabinH = 0.6;
                var paint = paints[_rng.Next(paints.Length)];
                double s = Math.Max(0.05, o.Spacing * 0.8);

                SampleBox(cx, cy, 0.3, width, length, bodyH, s, paint, 0.7);
                // 车舱：较短较窄，玻璃颜色深
                SampleBox(cx, cy - 0.2, 0.3 + bodyH, width * 0.85, length * 0.55, cabinH, s, new Rgb24(40, 55, 70), 0.15);
                // 车轮
                foreach (var (wx, wy) in new[] { (-1, -1), (-1, 1), (1, -1), (1, 1) })
                {
                    for (int k = 0; k < 120; k++)
                    {
                        double a = _rng.NextDouble() * Math.Tau;
                        Add(cx + wx * width / 2, cy + wy * length * 0.32 + 0.33 * Math.Cos(a), 0.33 + 0.33 * Math.Sin(a),
                            new Rgb24(25, 25, 25), ClassVehicle, 0.05);
                    }
                }
            }
        }

        private void SampleBox(double cx, double cy, double z0, double w, double l, double h, double s, Rgb24 color, double reflectance)
        {
            for (double y = -l / 2; y <= l / 2; y += s)
            for (double z = 0; z <= h; z += s)
            {
                Add(cx - w / 2, cy + y, z0 + z, color, ClassVehicle, reflectance);
                Add(cx + w / 2, cy + y, z0 + z, color, ClassVehicle, reflectance);
            }
            for (double x = -w / 2; x <= w / 2; x += s)
            {
                for (double z = 0; z <= h; z += s)
                {
                    Add(cx + x, cy - l / 2, z0 + z, color, ClassVehicle, reflectance);
                    Add(cx + x, cy + l / 2, z0 + z, color, ClassVehicle, reflectance);
                }
                for (double y = -l / 2; y <= l / 2; y += s)
                    Add(cx + x, cy + y, z0 + h, color, ClassVehicle, reflectance);
            }
        }

        public void Poles()
        {
            for (int i = 0; i < o.PoleCount; i++)
            {
                int side = i % 2 == 0 ? 1 : -1;
                double px = side * (HalfRoad + 0.4);
                double py = (i + 0.25) * o.RoadLength / o.PoleCount;
                const double height = 7.5;
                for (int k = 0; k < 1500; k++)
                {
                    double a = _rng.NextDouble() * Math.Tau, z = _rng.NextDouble() * height;
                    Add(px + 0.08 * Math.Cos(a), py + 0.08 * Math.Sin(a), o.CurbHeight + z, new Rgb24(200, 200, 205), ClassPole, 0.8);
                }
                // 灯臂伸向路中
                for (int k = 0; k < 300; k++)
                {
                    double t = _rng.NextDouble() * 1.8;
                    Add(px - side * t, py + Uniform(0.04), o.CurbHeight + height - 0.2 + t * 0.08, new Rgb24(200, 200, 205), ClassPole, 0.8);
                }
                for (int k = 0; k < 200; k++)
                    Add(px - side * (1.8 + Uniform(0.25)), py + Uniform(0.15), o.CurbHeight + height - 0.15 + Uniform(0.05),
                        new Rgb24(255, 240, 180), ClassPole, 0.95);
            }
        }

        public void Noise()
        {
            int n = (int)(Builder.Count * o.NoiseRatio);
            for (int i = 0; i < n; i++)
            {
                double x = Uniform(FacadeX + 4), y = _rng.NextDouble() * o.RoadLength;
                bool high = _rng.NextDouble() < 0.6;
                double z = high ? 3 + _rng.NextDouble() * 25 : -0.5 - _rng.NextDouble() * 4;
                Add(x, y, z, new Rgb24(255, 0, 255), high ? ClassHighNoise : ClassLowNoise, _rng.NextDouble());
            }
        }

        private double Uniform(double halfRange) => (_rng.NextDouble() * 2 - 1) * halfRange;

        private double Gaussian(double sigma)
        {
            double u1 = 1 - _rng.NextDouble(), u2 = _rng.NextDouble();
            return sigma * Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(Math.Tau * u2);
        }

        private (double X, double Y, double Z) RandomUnitVector()
        {
            double z = _rng.NextDouble() * 2 - 1, a = _rng.NextDouble() * Math.Tau, r = Math.Sqrt(1 - z * z);
            return (r * Math.Cos(a), r * Math.Sin(a), z);
        }

        private Rgb24 Jitter(Rgb24 c, int amount)
        {
            int d = _rng.Next(-amount, amount + 1);
            return new Rgb24((byte)Math.Clamp(c.R + d, 0, 255), (byte)Math.Clamp(c.G + d, 0, 255), (byte)Math.Clamp(c.B + d, 0, 255));
        }
    }
}
