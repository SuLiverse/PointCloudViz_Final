using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PointCloudViz_Final.Services
{
    public static class StreetGenerator
    {
        public static Task GenerateAsync(
            string filePath,
            StreetGenOptions? opt = null,
            CancellationToken token = default,
            IProgress<double>? progress = null)
        {
            opt ??= new StreetGenOptions();
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            if (ext == ".ply") return GeneratePlyAsync(filePath, opt, token, progress);
            return GenerateXyzAsync(filePath, opt, token, progress);
        }

        static async Task WriteXYZ(StreamWriter sw, double x, double y, double z, byte r, byte g, byte b, CultureInfo ci)
            => await sw.WriteLineAsync(string.Format(ci, "{0:F3} {1:F3} {2:F3} {3} {4} {5}", x, y, z, r, g, b));

        static async Task WritePLY(StreamWriter sw, double x, double y, double z, byte r, byte g, byte b, float intensity, CultureInfo ci)
            => await sw.WriteLineAsync(string.Format(ci, "{0:F6} {1:F6} {2:F6} {3} {4} {5} {6:F6}", x, y, z, r, g, b, intensity));

        static float ColorToIntensity(byte r, byte g, byte b, (double Min, double Max) range)
        {
            double Y = 0.299 * r + 0.587 * g + 0.114 * b;
            double t = Y / 255.0;
            return (float)(range.Min + (range.Max - range.Min) * t);
        }

        static (byte R, byte G, byte B) ChooseRoadColor(double x, double y, StreetGenOptions o)
        {
            bool dashOn = (y % o.CenterDashPeriod) < o.CenterDashOn;
            if (Math.Abs(x) < o.CenterHalfWidth && dashOn) return o.ColMarkingYellow;
            if (Math.Abs(Math.Abs(x) - o.EdgeOffset) < o.EdgeLineHalfWidth) return o.ColMarkingWhite;
            return o.ColAsphalt;
        }

        static long CountGround(StreetGenOptions o)
        {
            long nx = Math.Max(1, (long)Math.Floor(o.RoadWidth / o.Step) + 1);
            long ny = Math.Max(1, (long)Math.Floor(o.RoadLength / o.Step) + 1);
            return nx * ny;
        }
        static long CountSidewalk(StreetGenOptions o)
        {
            long nside = 2;
            long ny = Math.Max(1, (long)Math.Floor(o.RoadLength / o.Step) + 1);
            long nw = Math.Max(1, (long)Math.Floor(o.SidewalkWidth / o.Step) + 1);
            long curbExtra = 2 * ny * nside;
            return nside * ny * nw + curbExtra;
        }
        static long CountWalls(StreetGenOptions o)
        {
            if (!o.EnableBuildings) return 0;
            long nside = 2;
            long ny = Math.Max(1, (long)Math.Floor(o.RoadLength / (o.Step * 2)) + 1);
            long nz = Math.Max(1, (long)Math.Floor((o.BuildingHeight - o.CurbHeight) / (o.Step * 2)) + 1);
            return nside * ny * nz;
        }
        static long CountCars(StreetGenOptions o)   => o.EnableCars ? o.CarCount * 3500L : 0;
        static long CountTrees(StreetGenOptions o)  => o.EnableTrees ? o.TreeCount * 2500L : 0;
        static long CountPoles(StreetGenOptions o)  => o.EnablePoles ? o.PoleCount * 800L  : 0;

        static async Task ForGridAsync(double xmin, double xmax, double ymin, double ymax,
                                       double stepx, double stepy,
                                       Func<double, double, Task> body)
        {
            for (double y = ymin; y <= ymax; y += stepy)
                for (double x = xmin; x <= xmax; x += stepx)
                    await body(x, y);
        }
        static async Task ForYAsync(double ymin, double ymax, double stepy, Func<double, Task> body)
        {
            for (double y = ymin; y <= ymax; y += stepy)
                await body(y);
        }

        static async Task GenerateXyzAsync(string filePath, StreetGenOptions o, CancellationToken token, IProgress<double>? prog)
        {
            var rng = new Random(o.Seed);
            var ci  = CultureInfo.InvariantCulture;

            long total = CountGround(o) + CountSidewalk(o) + CountWalls(o)
                       + CountCars(o) + CountTrees(o) + CountPoles(o);
            double done = 0;

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath))!);
            using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 1<<16, useAsync:true);
            using var sw = new StreamWriter(fs, new UTF8Encoding(false));

            await ForGridAsync(-o.RoadWidth/2, o.RoadWidth/2, 0, o.RoadLength, o.Step, o.Step, async (x,y)=>
            {
                token.ThrowIfCancellationRequested();
                double z = rng.NextDouble()*o.AsphaltNoiseAmp;
                var (r,g,b) = ChooseRoadColor(x,y,o);
                await WriteXYZ(sw,x,y,z,r,g,b,ci);
                if ((++done % 50000)==0) prog?.Report(done/total);
            });

            await ForYAsync(0, o.RoadLength, o.Step, async y =>
            {
                token.ThrowIfCancellationRequested();
                foreach (var s in new[] { -1.0, 1.0 })
                {
                    for (double w=0; w<o.SidewalkWidth; w+=o.Step)
                    {
                        double x = s*(o.RoadWidth/2 + w);
                        double z = o.CurbHeight + rng.NextDouble()*o.SidewalkNoiseAmp;
                        await WriteXYZ(sw,x,y,z, o.ColSidewalk.R,o.ColSidewalk.G,o.ColSidewalk.B, ci);
                        if (w < o.Step*2)
                        {
                            await WriteXYZ(sw,x,y,0.05, 120,120,120, ci);
                            await WriteXYZ(sw,x,y,0.10, 120,120,120, ci);
                        }
                        if ((++done % 50000)==0) prog?.Report(done/total);
                    }
                }
            });

            if (o.EnableBuildings)
            {
                await ForYAsync(0, o.RoadLength, o.Step*2, async y =>
                {
                    token.ThrowIfCancellationRequested();
                    foreach (var s in new[] { -1.0, 1.0 })
                    {
                        double bx = s * (o.RoadWidth/2 + o.SidewalkWidth);
                        for (double z=o.CurbHeight; z<o.BuildingHeight; z+=o.Step*2)
                        {
                            double x = bx + Math.Sin(y)*o.WallWaveAmp;
                            await WriteXYZ(sw,x,y,z, o.ColBuilding.R,o.ColBuilding.G,o.ColBuilding.B, ci);
                            if ((++done % 50000)==0) prog?.Report(done/total);
                        }
                    }
                });
            }

            if (o.EnableCars && o.CarCount>0)
            {
                for (int i=0;i<o.CarCount;i++)
                {
                    token.ThrowIfCancellationRequested();
                    bool right = rng.NextDouble()>0.5;
                    double cy = 2.0 + rng.NextDouble()*(o.RoadLength-4.0);
                    double cx = (right? +1 : -1) * (o.EdgeOffset-0.6);
                    double L = 3.8 + rng.NextDouble()*1.0;
                    double W = 1.6 + rng.NextDouble()*0.3;
                    double H = 1.45 + rng.NextDouble()*0.25;
                    var (cr,cg,cb) = o.CarColors[rng.Next(o.CarColors.Length)];
                    double ds = Math.Max(0.12, o.Step);
                    for (double z=0; z<=H; z+=ds)
                    {
                        for (double x=-W/2; x<=W/2; x+=ds)
                        {
                            await WriteXYZ(sw, cx+x, cy-L/2, z, cr,cg,cb, ci);
                            await WriteXYZ(sw, cx+x, cy+L/2, z, cr,cg,cb, ci);
                        }
                        for (double y=-L/2; y<=L/2; y+=ds)
                        {
                            await WriteXYZ(sw, cx-W/2, cy+y, z, cr,cg,cb, ci);
                            await WriteXYZ(sw, cx+W/2, cy+y, z, cr,cg,cb, ci);
                        }
                        if ((++done % 40000)==0) prog?.Report(done/total);
                    }
                }
            }

            if (o.EnableTrees && o.TreeCount>0)
            {
                for (int i=0;i<o.TreeCount;i++)
                {
                    token.ThrowIfCancellationRequested();
                    bool right = rng.NextDouble()>0.5;
                    double ty = 1.5 + rng.NextDouble()*(o.RoadLength-3.0);
                    double tx = (right? +1 : -1) * (o.RoadWidth/2 + o.SidewalkWidth + 0.5);

                    double r = 0.15, h = 2.4;
                    int nTrunk = 800;
                    for (int k=0;k<nTrunk;k++)
                    {
                        double t = rng.NextDouble()*Math.PI*2;
                        double z = rng.NextDouble()*h;
                        double x = tx + r*Math.Cos(t);
                        double y = ty + r*Math.Sin(t);
                        await WriteXYZ(sw, x,y, o.CurbHeight+z, o.ColTreeTrunk.R,o.ColTreeTrunk.G,o.ColTreeTrunk.B, ci);
                    }
                    int nLeaf = 2000;
                    double cx = tx, cy = ty, cz = o.CurbHeight + h + 0.9;
                    for (int k=0;k<nLeaf;k++)
                    {
                        double x = cx + (rng.NextDouble()-0.5)*2.2;
                        double y = cy + (rng.NextDouble()-0.5)*2.2;
                        double z = cz + (rng.NextDouble()-0.5)*1.8;
                        await WriteXYZ(sw, x,y,z, o.ColTreeLeaf.R,o.ColTreeLeaf.G,o.ColTreeLeaf.B, ci);
                    }
                    if ((++done % 40000)==0) prog?.Report(done/total);
                }
            }

            if (o.EnablePoles && o.PoleCount>0)
            {
                for (int i=0;i<o.PoleCount;i++)
                {
                    token.ThrowIfCancellationRequested();
                    bool right = rng.NextDouble()>0.5;
                    double py = 1.2 + rng.NextDouble()*(o.RoadLength-2.4);
                    double px = (right? +1 : -1) * (o.RoadWidth/2 + o.SidewalkWidth - 0.6);
                    double r = 0.07, h=3.2;
                    int n = 800;
                    for (int k=0;k<n;k++)
                    {
                        double t = rng.NextDouble()*Math.PI*2;
                        double z = rng.NextDouble()*h;
                        double x = px + r*Math.Cos(t);
                        double y = py + r*Math.Sin(t);
                        await WriteXYZ(sw, x,y, o.CurbHeight+z, o.ColPole.R,o.ColPole.G,o.ColPole.B, ci);
                    }
                    if ((++done % 40000)==0) prog?.Report(done/total);
                }
            }

            prog?.Report(1.0);
        }

        static async Task GeneratePlyAsync(string filePath, StreetGenOptions o, CancellationToken token, IProgress<double>? prog)
        {
            var rng = new Random(o.Seed);
            var ci  = CultureInfo.InvariantCulture;

            long total = CountGround(o) + CountSidewalk(o) + CountWalls(o)
                       + CountCars(o) + CountTrees(o) + CountPoles(o);
            double done = 0;

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath))!);
            using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 1<<16, useAsync:true);
            using var sw = new StreamWriter(fs, new UTF8Encoding(false));

            await sw.WriteLineAsync("ply");
            await sw.WriteLineAsync("format ascii 1.0");
            await sw.WriteLineAsync("comment Synthetic street scene generated in-app");
            await sw.WriteLineAsync($"element vertex {total}");
            await sw.WriteLineAsync("property float x");
            await sw.WriteLineAsync("property float y");
            await sw.WriteLineAsync("property float z");
            await sw.WriteLineAsync("property uchar red");
            await sw.WriteLineAsync("property uchar green");
            await sw.WriteLineAsync("property uchar blue");
            await sw.WriteLineAsync("property float intensity");
            await sw.WriteLineAsync("end_header");

            await ForGridAsync(-o.RoadWidth/2, o.RoadWidth/2, 0, o.RoadLength, o.Step, o.Step, async (x,y)=>
            {
                token.ThrowIfCancellationRequested();
                double z = rng.NextDouble()*o.AsphaltNoiseAmp;
                var (r,g,b) = ChooseRoadColor(x,y,o);
                float I = ColorToIntensity(r,g,b,o.IntensityRange);
                await WritePLY(sw,x,y,z,r,g,b,I,ci);
                if ((++done % 50000)==0) prog?.Report(done/total);
            });

            await ForYAsync(0, o.RoadLength, o.Step, async y =>
            {
                token.ThrowIfCancellationRequested();
                foreach (var s in new[] { -1.0, 1.0 })
                {
                    for (double w=0; w<o.SidewalkWidth; w+=o.Step)
                    {
                        double x = s*(o.RoadWidth/2 + w);
                        double z = o.CurbHeight + rng.NextDouble()*o.SidewalkNoiseAmp;
                        var (r,g,b) = o.ColSidewalk;
                        await WritePLY(sw,x,y,z,r,g,b, ColorToIntensity(r,g,b,o.IntensityRange), ci);

                        if (w < o.Step*2)
                        {
                            var I = ColorToIntensity(120,120,120, o.IntensityRange);
                            await WritePLY(sw,x,y,0.05, 120,120,120, I, ci);
                            await WritePLY(sw,x,y,0.10, 120,120,120, I, ci);
                        }
                        if ((++done % 50000)==0) prog?.Report(done/total);
                    }
                }
            });

            if (o.EnableBuildings)
            {
                await ForYAsync(0, o.RoadLength, o.Step*2, async y =>
                {
                    token.ThrowIfCancellationRequested();
                    foreach (var s in new[] { -1.0, 1.0 })
                    {
                        double bx = s * (o.RoadWidth/2 + o.SidewalkWidth);
                        for (double z=o.CurbHeight; z<o.BuildingHeight; z+=o.Step*2)
                        {
                            double x = bx + Math.Sin(y)*o.WallWaveAmp;
                            var (r,g,b) = o.ColBuilding;
                            await WritePLY(sw,x,y,z,r,g,b, ColorToIntensity(r,g,b,o.IntensityRange), ci);
                            if ((++done % 50000)==0) prog?.Report(done/total);
                        }
                    }
                });
            }

            if (o.EnableCars && o.CarCount>0)
            {
                for (int i=0;i<o.CarCount;i++)
                {
                    token.ThrowIfCancellationRequested();
                    bool right = rng.NextDouble()>0.5;
                    double cy = 2.0 + rng.NextDouble()*(o.RoadLength-4.0);
                    double cx = (right? +1 : -1) * (o.EdgeOffset-0.6);
                    double L = 3.8 + rng.NextDouble()*1.0;
                    double W = 1.6 + rng.NextDouble()*0.3;
                    double H = 1.45 + rng.NextDouble()*0.25;
                    var (cr,cg,cb) = o.CarColors[rng.Next(o.CarColors.Length)];
                    float I = ColorToIntensity(cr,cg,cb,o.IntensityRange);
                    double ds = Math.Max(0.12, o.Step);
                    for (double z=0; z<=H; z+=ds)
                    {
                        for (double x=-W/2; x<=W/2; x+=ds)
                        {
                            await WritePLY(sw, cx+x, cy-L/2, z, cr,cg,cb, I, ci);
                            await WritePLY(sw, cx+x, cy+L/2, z, cr,cg,cb, I, ci);
                        }
                        for (double y=-L/2; y<=L/2; y+=ds)
                        {
                            await WritePLY(sw, cx-W/2, cy+y, z, cr,cg,cb, I, ci);
                            await WritePLY(sw, cx+W/2, cy+y, z, cr,cg,cb, I, ci);
                        }
                        if ((++done % 40000)==0) prog?.Report(done/total);
                    }
                }
            }

            if (o.EnableTrees && o.TreeCount>0)
            {
                for (int i=0;i<o.TreeCount;i++)
                {
                    token.ThrowIfCancellationRequested();
                    bool right = rng.NextDouble()>0.5;
                    double ty = 1.5 + rng.NextDouble()*(o.RoadLength-3.0);
                    double tx = (right? +1 : -1) * (o.RoadWidth/2 + o.SidewalkWidth + 0.5);

                    double r = 0.15, h = 2.4;
                    int nTrunk = 800;
                    var (tr,tg,tb) = o.ColTreeTrunk;
                    float It = ColorToIntensity(tr,tg,tb,o.IntensityRange);
                    for (int k=0;k<nTrunk;k++)
                    {
                        double t = rng.NextDouble()*Math.PI*2;
                        double z = rng.NextDouble()*h;
                        double x = tx + r*Math.Cos(t);
                        double y = ty + r*Math.Sin(t);
                        await WritePLY(sw, x,y, o.CurbHeight+z, tr,tg,tb, It, ci);
                    }
                    int nLeaf = 2000;
                    var (lr,lg,lb) = o.ColTreeLeaf;
                    float Il = ColorToIntensity(lr,lg,lb,o.IntensityRange);
                    double cx = tx, cy = ty, cz = o.CurbHeight + h + 0.9;
                    for (int k=0;k<nLeaf;k++)
                    {
                        double x = cx + (rng.NextDouble()-0.5)*2.2;
                        double y = cy + (rng.NextDouble()-0.5)*2.2;
                        double z = cz + (rng.NextDouble()-0.5)*1.8;
                        await WritePLY(sw, x,y,z, lr,lg,lb, Il, ci);
                    }
                    if ((++done % 40000)==0) prog?.Report(done/total);
                }
            }

            if (o.EnablePoles && o.PoleCount>0)
            {
                var (pr,pg,pb) = o.ColPole;
                float Ip = ColorToIntensity(pr,pg,pb,o.IntensityRange);
                for (int i=0;i<o.PoleCount;i++)
                {
                    token.ThrowIfCancellationRequested();
                    bool right = rng.NextDouble()>0.5;
                    double py = 1.2 + rng.NextDouble()*(o.RoadLength-2.4);
                    double px = (right? +1 : -1) * (o.RoadWidth/2 + o.SidewalkWidth - 0.6);
                    double r = 0.07, h=3.2;
                    int n = 800;
                    for (int k=0;k<n;k++)
                    {
                        double t = rng.NextDouble()*Math.PI*2;
                        double z = rng.NextDouble()*h;
                        double x = px + r*Math.Cos(t);
                        double y = py + r*Math.Sin(t);
                        await WritePLY(sw, x,y, o.CurbHeight+z, pr,pg,pb, Ip, ci);
                    }
                    if ((++done % 40000)==0) prog?.Report(done/total);
                }
            }

            prog?.Report(1.0);
        }
    }
}
