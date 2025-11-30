using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using PointCloudViz_Final.Models;

namespace PointCloudViz_Final.IO
{
    /// <summary>流式LAS读取器：支持进度报告和异步加载（解决加载卡顿）</summary>
    public class StreamingLasReader : IPointReader
    {
        private int _targetPointCount = 2_000_000;

        /// <summary>目标采样点数，用于计算下采样步长（可根据硬件手动调整）。</summary>
        public int TargetPointCount
        {
            get => _targetPointCount;
            set => _targetPointCount = Math.Max(1, value);
        }

        public string Name => "LAS (Streaming)";
        public bool CanRead(string ext) => ext.Equals(".las", StringComparison.OrdinalIgnoreCase);

        public async Task<PointCloud> ReadAsync(string path, CancellationToken token)
        {
            return await ReadAsyncInternal(path, token, null);
        }

        /// <summary>带进度报告的流式读取（扩展方法）</summary>
        public async Task<PointCloud> ReadAsync(string path, CancellationToken token, IProgress<double>? progress)
        {
            return await ReadAsyncInternal(path, token, progress);
        }

        /// <summary>内部读取实现</summary>
        private async Task<PointCloud> ReadAsyncInternal(string path, CancellationToken token, IProgress<double>? progress)
        {
            const int bufferSize = 65536; // 64KB缓冲区
            var pts = new List<PointRecord>();

            await Task.Run(() =>
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, true);
                using var br = new BinaryReader(fs);

                // 读取LAS头部
                var header = ReadLasHeader(br, fs);
                if (header.PointCount <= 0)
                    throw new InvalidDataException("无效的点数");

                // 跳到点数据起始
                fs.Seek(header.OffsetToPointData, SeekOrigin.Begin);

                long totalBytes = fs.Length;
                long readBytes = header.OffsetToPointData;
                if (header.PointFormat is not (0 or 1 or 2 or 3))
                    throw new NotSupportedException($"不支持的点格式：{header.PointFormat}（仅支持 0/1/2/3）");

                ulong pointCount = header.PointCount;
                int step = Math.Max(1, (int)Math.Ceiling(pointCount / (double)_targetPointCount));

                // 流式读取点记录
                for (ulong i = 0; i < pointCount; i++)
                {
                    if (i % 10000 == 0)
                    {
                        token.ThrowIfCancellationRequested();
                        
                        // 报告进度
                        if (progress != null && totalBytes > 0)
                        {
                            double percent = (double)readBytes / totalBytes * 100.0;
                            progress.Report(percent);
                        }
                    }

                    long recordStart = fs.Position;

                    // 读取点数据
                    int ix = br.ReadInt32();
                    int iy = br.ReadInt32();
                    int iz = br.ReadInt32();
                    ushort intensity = br.ReadUInt16();
                    byte flags = br.ReadByte();
                    byte classification = br.ReadByte();

                    // 过滤无效点（分类7=噪声，18=高程噪声）
                    if (classification == 7 || classification == 18)
                    {
                        // 跳过剩余字节
                        int remain = header.PointRecordLength - (int)(fs.Position - recordStart);
                        if (remain > 0) br.ReadBytes(remain);
                        readBytes += header.PointRecordLength;
                        continue;
                    }

                    br.ReadByte(); // scanAngle
                    br.ReadByte(); // userData
                    br.ReadUInt16(); // pointSourceId

                    // 读取可选字段
                    double gpsTime = 0;
                    ushort r16 = 0, g16 = 0, b16 = 0;

                    switch (header.PointFormat)
                    {
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

                    // 跳过剩余字节
                    long consumed = fs.Position - recordStart;
                    int remain2 = header.PointRecordLength - (int)consumed;
                    if (remain2 > 0) br.ReadBytes(remain2);

                    readBytes += header.PointRecordLength;

                    if (i % step != 0) continue; // 下采样

                    // 还原坐标
                    float x = (float)(ix * header.ScaleX + header.OffsetX);
                    float y = (float)(iy * header.ScaleY + header.OffsetY);
                    float z = (float)(iz * header.ScaleZ + header.OffsetZ);
                    float inten = intensity / 65535f;

                    Color color = (header.PointFormat is 2 or 3)
                        ? Color.FromRgb((byte)(r16 >> 8), (byte)(g16 >> 8), (byte)(b16 >> 8))
                        : Colors.White;

                    pts.Add(new PointRecord(x, y, z, inten, color));
                }

                progress?.Report(100.0);
            }, token);

            return new PointCloud(pts);
        }

        private LasHeader ReadLasHeader(BinaryReader br, FileStream fs)
        {
            var sig = new string(br.ReadChars(4));
            if (sig != "LASF")
                throw new InvalidDataException("不是有效的 LAS 文件");

            br.ReadUInt16(); // File Source ID
            br.ReadUInt16(); // Global Encoding
            br.ReadBytes(16); // Project ID
            byte verMajor = br.ReadByte();
            byte verMinor = br.ReadByte();

            br.ReadBytes(32); // System Identifier
            br.ReadBytes(32); // Generating Software
            br.ReadUInt16(); // File Creation Day
            br.ReadUInt16(); // File Creation Year

            ushort headerSize = br.ReadUInt16();
            uint offsetToPointData = br.ReadUInt32();
            uint vlrCount = br.ReadUInt32();

            byte pointFormat = br.ReadByte();
            ushort pointRecordLength = br.ReadUInt16();
            uint legacyPointCount = br.ReadUInt32();
            br.ReadBytes(20);

            double scaleX = br.ReadDouble();
            double scaleY = br.ReadDouble();
            double scaleZ = br.ReadDouble();
            double offX = br.ReadDouble();
            double offY = br.ReadDouble();
            double offZ = br.ReadDouble();

            br.ReadBytes(48); // Max/Min (跳过)

            ulong extendedPointCount = 0;
            if (verMajor > 1 || (verMajor == 1 && verMinor >= 4))
            {
                // LAS 1.4 增加了扩展字段；至少需要读取扩展点计数以避免溢出
                br.ReadUInt64(); // Start of Waveform Data Packet Record
                br.ReadUInt64(); // Start of First Extended VLR
                br.ReadUInt32(); // Number of Extended VLRs
                extendedPointCount = br.ReadUInt64();

                // 15 个返回计数（每个8字节）
                for (int i = 0; i < 15; i++)
                {
                    br.ReadUInt64();
                }
            }

            // 确保文件指针位于头部末尾
            if (headerSize > fs.Position)
            {
                br.ReadBytes((int)(headerSize - fs.Position));
            }

            var pointCount = extendedPointCount > 0 ? extendedPointCount : legacyPointCount;
            return new LasHeader
            {
                PointCount = pointCount,
                PointFormat = pointFormat,
                PointRecordLength = pointRecordLength,
                OffsetToPointData = offsetToPointData,
                ScaleX = scaleX,
                ScaleY = scaleY,
                ScaleZ = scaleZ,
                OffsetX = offX,
                OffsetY = offY,
                OffsetZ = offZ
            };
        }

        private class LasHeader
        {
            public ulong PointCount { get; set; }
            public byte PointFormat { get; set; }
            public ushort PointRecordLength { get; set; }
            public uint OffsetToPointData { get; set; }
            public double ScaleX { get; set; }
            public double ScaleY { get; set; }
            public double ScaleZ { get; set; }
            public double OffsetX { get; set; }
            public double OffsetY { get; set; }
            public double OffsetZ { get; set; }
        }
    }
}

