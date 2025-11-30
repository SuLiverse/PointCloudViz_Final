using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using PointCloudViz_Final.Models;

namespace PointCloudViz_Final.IO
{
   
    public class BasicLasReader : IPointReader
    {
        public string Name => "LAS (built-in)";
        public bool CanRead(string ext) => ext.Equals(".las", StringComparison.OrdinalIgnoreCase);

        public async Task<PointCloud> ReadAsync(string path, CancellationToken token)
        {
            return await Task.Run(() =>
            {
                using var fs = File.OpenRead(path);
                using var br = new BinaryReader(fs);

                // --- Header ---
                var sig = new string(br.ReadChars(4));
                if (sig != "LASF")
                    throw new InvalidDataException("不是有效的 LAS 文件（缺少 LASF 签名）。");

                br.ReadUInt16(); // File Source ID
                br.ReadUInt16(); // Global Encoding
                br.ReadBytes(16); // Project ID (GUID)
                byte verMajor = br.ReadByte();
                byte verMinor = br.ReadByte();
                // 仅做个提示，不强制报错
                if (verMajor == 1 && verMinor >= 4)
                    throw new NotSupportedException("当前内置读取器不支持 LAS 1.4，请用 CloudCompare/PDAL 转换或换库。");

                br.ReadBytes(32); // System Identifier
                br.ReadBytes(32); // Generating Software
                br.ReadUInt16();  // File Creation Day of Year
                br.ReadUInt16();  // File Creation Year

                ushort headerSize = br.ReadUInt16();          // Header Size
                uint offsetToPointData = br.ReadUInt32();     // Offset to point data
                uint vlrCount = br.ReadUInt32();              // Variable length records

                byte pointFormat = br.ReadByte();             // Point Data Format ID (0/1/2/3)
                ushort pointRecordLength = br.ReadUInt16();   // Point Data Record Length
                uint legacyPointCount = br.ReadUInt32();      // Legacy number of point records
                br.ReadBytes(20);                             // legacy points by return[5]

                // Scale / Offset (double，小端)
                double scaleX = br.ReadDouble();
                double scaleY = br.ReadDouble();
                double scaleZ = br.ReadDouble();
                double offX = br.ReadDouble();
                double offY = br.ReadDouble();
                double offZ = br.ReadDouble();

                // Max/Min（这里不强依赖）
                double maxX = br.ReadDouble();
                double minX = br.ReadDouble();
                double maxY = br.ReadDouble();
                double minY = br.ReadDouble();
                double maxZ = br.ReadDouble();
                double minZ = br.ReadDouble();

                // 跳到点数据起始
                fs.Seek(offsetToPointData, SeekOrigin.Begin);

                // 简易：用 legacyPointCount 作为点数（LAS 1.0–1.3 OK）
                long pointCount = legacyPointCount;
                if (pointCount <= 0)
                    throw new NotSupportedException("点数为 0 或未知；该文件可能为 LAS 1.4，当前读取器不支持。");

                // 只支持格式 0/1/2/3
                if (pointFormat is not (0 or 1 or 2 or 3))
                    throw new NotSupportedException($"不支持的点格式：{pointFormat}（仅支持 0/1/2/3）");

                // 读取 - 优化：自适应下采样大文件
                var pts = new List<PointRecord>((int)Math.Min(pointCount, 2_000_000));
                // 自适应步进：超大文件自动下采样以提高读取速度
                int step = pointCount > 10_000_000 ? 2 : pointCount > 5_000_000 ? 1 : 1;

                for (long i = 0; i < pointCount; i++)
                {
                    if (i % 10000 == 0) token.ThrowIfCancellationRequested();

                    long recordStart = fs.Position;

                    // --- 公共字段 ---
                    int ix = br.ReadInt32();
                    int iy = br.ReadInt32();
                    int iz = br.ReadInt32();

                    ushort intensity = br.ReadUInt16(); // 0..65535

                    // 下面这些字节我们不严格使用（跳读即可）
                    byte flags = br.ReadByte();         // return bits 等
                    byte classification = br.ReadByte();
                    sbyte scanAngle = (sbyte)br.ReadByte();
                    byte userData = br.ReadByte();
                    ushort pointSourceId = br.ReadUInt16();

                    // 可选：GPS time / RGB
                    double gpsTime = 0;
                    ushort r16 = 0, g16 = 0, b16 = 0;

                    switch (pointFormat)
                    {
                        case 0:
                            // 无附加字段
                            break;
                        case 1:
                            gpsTime = br.ReadDouble();
                            break;
                        case 2:
                            r16 = br.ReadUInt16();
                            g16 = br.ReadUInt16();
                            b16 = br.ReadUInt16();
                            break;
                        case 3:
                            gpsTime = br.ReadDouble();
                            r16 = br.ReadUInt16();
                            g16 = br.ReadUInt16();
                            b16 = br.ReadUInt16();
                            break;
                    }

                    // 如果还有额外字节，跳过到记录尾
                    long consumed = fs.Position - recordStart;
                    int remain = pointRecordLength - (int)consumed;
                    if (remain > 0) br.ReadBytes(remain);

                    if (i % step != 0) continue; // 下采样（如有需要）

                    // 还原实际坐标
                    float x = (float)(ix * scaleX + offX);
                    float y = (float)(iy * scaleY + offY);
                    float z = (float)(iz * scaleZ + offZ);

                    // Intensity 归一化
                    float inten = intensity / 65535f;

                    // 颜色：LAS RGB 是 16bit，简单映射到 8bit（右移 8 位）
                    byte rr = (byte)(r16 >> 8);
                    byte gg = (byte)(g16 >> 8);
                    byte bb = (byte)(b16 >> 8);

                    // 如果没有颜色，给个根据强度的灰度/或让后续 ColorMap 决定
                    Color color = (pointFormat is 2 or 3)
                        ? Color.FromRgb(rr, gg, bb)
                        : Colors.White;

                    pts.Add(new PointRecord(x, y, z, inten, color));
                }

                return new PointCloud(pts);
            });
        }
    }
}
