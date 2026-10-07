using System.Windows.Media;
using PointCloudViz_Final.Models;

namespace PointCloudViz_Final.Services;

public static class DemoSceneFactory
{
    public static PointCloud Create(int seed = 2026, CancellationToken token = default)
    {
        var random = new Random(seed);
        var points = new List<PointRecord>(250_000);
        void Add(double x, double y, double z, Color color)
        {
            if ((points.Count & 4095) == 0) token.ThrowIfCancellationRequested();
            double noise = (random.NextDouble() - .5) * .018;
            float intensity = (color.R * .299f + color.G * .587f + color.B * .114f) / 255;
            points.Add(new((float)(x + noise), (float)(y + noise), (float)(z + noise), intensity, color));
        }
        var road = Color.FromRgb(79, 90, 99);
        var paving = Color.FromRgb(145, 161, 164);
        var marking = Color.FromRgb(229, 219, 151);
        for (double x = -23; x <= 23; x += .26)
        for (double y = -22; y <= 22; y += .26)
        {
            bool isRoad = Math.Abs(x) < 3.5 || Math.Abs(y) < 3.5;
            bool line = (Math.Abs(x) < .12 && ((int)(y + 24) % 5 < 3)) ||
                (Math.Abs(y) < .12 && ((int)(x + 24) % 5 < 3));
            Add(x, y, isRoad ? 0 : .15, line ? marking : isRoad ? road : paving);
        }
        void Building(double x, double y, double width, double depth, double height, Color facade)
        {
            var glass = Color.FromRgb(69, 123, 143);
            for (double z = .2; z <= height; z += .18)
            {
                for (double u = 0; u <= width; u += .18)
                {
                    bool window = u % 2.8 > .45 && u % 2.8 < 2.25 && z % 3.2 > .65 && z % 3.2 < 2.55;
                    var color = window ? glass : facade;
                    Add(x + u, y, z, color);
                    Add(x + u, y + depth, z, color);
                }
                for (double v = 0; v <= depth; v += .18)
                {
                    bool window = v % 2.8 > .45 && v % 2.8 < 2.25 && z % 3.2 > .65 && z % 3.2 < 2.55;
                    var color = window ? glass : facade;
                    Add(x, y + v, z, color);
                    Add(x + width, y + v, z, color);
                }
            }
            for (double u = 0; u <= width; u += .2)
            for (double v = 0; v <= depth; v += .2)
            {
                bool panel = u > 1 && u < width - 1 && v > 1 && v < depth - 1 && ((int)(u * 2) % 6 < 4);
                Add(x + u, y + v, height + (panel ? .15 : 0), panel ? glass : Color.FromRgb(207, 215, 216));
            }
            for (double u = -.15; u <= width + .15; u += .12)
            {
                Add(x + u, y - .15, height + .4, facade);
                Add(x + u, y + depth + .15, height + .4, facade);
            }
        }
        Building(-19, 6, 12, 13, 20, Color.FromRgb(193, 210, 210));
        Building(6, 7, 13, 11, 12, Color.FromRgb(184, 196, 210));
        Building(-19, -18, 11, 12, 9, Color.FromRgb(217, 201, 179));
        Building(6, -18, 12, 11, 6, Color.FromRgb(194, 207, 190));
        void Tree(double x, double y, double height)
        {
            for (int i = 0; i < 350; i++)
            {
                double angle = random.NextDouble() * Math.PI * 2;
                Add(x + .16 * Math.Cos(angle), y + .16 * Math.Sin(angle),
                    random.NextDouble() * height, Color.FromRgb(139, 119, 91));
            }
            for (int i = 0; i < 2200; i++)
            {
                double angle = random.NextDouble() * Math.PI * 2;
                double cosine = random.NextDouble() * 2 - 1;
                double radius = 1.4 + random.NextDouble() * .6;
                double sine = Math.Sqrt(1 - cosine * cosine);
                Add(x + radius * sine * Math.Cos(angle), y + radius * sine * Math.Sin(angle),
                    height + radius * cosine, Color.FromRgb((byte)(68 + random.Next(30)), (byte)(132 + random.Next(45)), 98));
            }
        }
        foreach (var (x, y) in new (double, double)[] { (-21,-3), (-13,-4.5), (-5,-12), (-5,13), (4.5,13), (20,4), (20,-5), (6,-21) })
            Tree(x, y, 3.5 + random.NextDouble());
        for (int car = 0; car < 6; car++)
        {
            double x = car % 2 == 0 ? -1.8 : 1.8, y = -16 + car * 6;
            var color = car % 3 == 0 ? Color.FromRgb(226, 141, 92) :
                car % 3 == 1 ? Color.FromRgb(81, 157, 191) : Color.FromRgb(221, 229, 224);
            for (int i = 0; i < 1500; i++)
            {
                double u = random.NextDouble() * 1.6 - .8, v = random.NextDouble() * 3.5 - 1.75;
                double z = random.NextDouble() * 1.3 + .25;
                if (i % 3 == 0) u = Math.Sign(u) * .8;
                else if (i % 3 == 1) v = Math.Sign(v) * 1.75;
                else z = 1.55;
                Add(x + u, y + v, z, color);
            }
        }
        token.ThrowIfCancellationRequested();
        return new PointCloud(points);
    }
}
