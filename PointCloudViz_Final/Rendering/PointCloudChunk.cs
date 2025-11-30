using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Windows.Media;
using PointCloudViz_Final.Models;

namespace PointCloudViz_Final.Rendering
{
    /// <summary>点云块：分块管理，支持量化压缩和一次上传</summary>
    public class PointCloudChunk
    {
        public BoundingBox Bounds { get; set; }
        public int PointCount => _quantizedPoints != null ? _quantizedPoints.Length / 10 : 0; // 每点10字节
        public int LodLevel { get; set; } = 0;
        public bool IsUploaded { get; private set; } = false;
        public DateTime LastAccessTime { get; set; } = DateTime.Now;

        // 量化后的点数据（每点8-10字节）
        private byte[]? _quantizedPoints;
        private Vector3 _bboxMin;
        private Vector3 _bboxSize;

        // 原始点数据（上传后可释放）
        private List<PointRecord>? _originalPoints;

        /// <summary>从点列表创建块</summary>
        public static PointCloudChunk Create(IReadOnlyList<PointRecord> points, BoundingBox bounds, int lodLevel = 0)
        {
            var chunk = new PointCloudChunk
            {
                Bounds = bounds,
                LodLevel = lodLevel,
                _originalPoints = new List<PointRecord>(points)
            };

            chunk.QuantizePoints();
            return chunk;
        }

        /// <summary>量化压缩点数据：位置16位，颜色8位，强度16位</summary>
        private void QuantizePoints()
        {
            if (_originalPoints == null || _originalPoints.Count == 0)
            {
                _quantizedPoints = Array.Empty<byte>();
                return;
            }

            _bboxMin = new Vector3(Bounds.MinX, Bounds.MinY, Bounds.MinZ);
            _bboxSize = new Vector3(
                Math.Max(Bounds.MaxX - Bounds.MinX, 0.001f),
                Math.Max(Bounds.MaxY - Bounds.MinY, 0.001f),
                Math.Max(Bounds.MaxZ - Bounds.MinZ, 0.001f)
            );

            // 每点：pos16(6字节) + color(4字节) + intensity16(2字节) = 12字节
            // 优化：pos16(6字节) + color+intensity(4字节) = 10字节（强度并入颜色alpha）
            int bytesPerPoint = 10;
            _quantizedPoints = new byte[_originalPoints.Count * bytesPerPoint];

            for (int i = 0; i < _originalPoints.Count; i++)
            {
                var p = _originalPoints[i];
                int offset = i * bytesPerPoint;

                // 位置量化到16位（相对bbox）
                ushort px = (ushort)Math.Clamp((p.X - _bboxMin.X) / _bboxSize.X * 65535, 0, 65535);
                ushort py = (ushort)Math.Clamp((p.Y - _bboxMin.Y) / _bboxSize.Y * 65535, 0, 65535);
                ushort pz = (ushort)Math.Clamp((p.Z - _bboxMin.Z) / _bboxSize.Z * 65535, 0, 65535);

                // 写入位置（6字节：R16G16B16）
                _quantizedPoints[offset + 0] = (byte)(px & 0xFF);
                _quantizedPoints[offset + 1] = (byte)(px >> 8);
                _quantizedPoints[offset + 2] = (byte)(py & 0xFF);
                _quantizedPoints[offset + 3] = (byte)(py >> 8);
                _quantizedPoints[offset + 4] = (byte)(pz & 0xFF);
                _quantizedPoints[offset + 5] = (byte)(pz >> 8);

                // 颜色（3字节RGB）+ 强度并入alpha（1字节）
                byte intensityByte = (byte)Math.Clamp(p.Intensity * 255, 0, 255);
                _quantizedPoints[offset + 6] = p.Color.R;
                _quantizedPoints[offset + 7] = p.Color.G;
                _quantizedPoints[offset + 8] = p.Color.B;
                _quantizedPoints[offset + 9] = intensityByte;
            }
        }

        /// <summary>解码量化点（用于渲染）</summary>
        public IEnumerable<PointRecord> DecodePoints()
        {
            if (_quantizedPoints == null || _quantizedPoints.Length == 0)
                yield break;

            int bytesPerPoint = 10;
            int pointCount = _quantizedPoints.Length / bytesPerPoint;

            for (int i = 0; i < pointCount; i++)
            {
                int offset = i * bytesPerPoint;

                // 读取位置（16位）
                ushort px = (ushort)(_quantizedPoints[offset + 0] | (_quantizedPoints[offset + 1] << 8));
                ushort py = (ushort)(_quantizedPoints[offset + 2] | (_quantizedPoints[offset + 3] << 8));
                ushort pz = (ushort)(_quantizedPoints[offset + 4] | (_quantizedPoints[offset + 5] << 8));

                // 解码位置
                float x = _bboxMin.X + (px / 65535.0f) * _bboxSize.X;
                float y = _bboxMin.Y + (py / 65535.0f) * _bboxSize.Y;
                float z = _bboxMin.Z + (pz / 65535.0f) * _bboxSize.Z;

                // 读取颜色和强度
                byte r = _quantizedPoints[offset + 6];
                byte g = _quantizedPoints[offset + 7];
                byte b = _quantizedPoints[offset + 8];
                float intensity = _quantizedPoints[offset + 9] / 255.0f;

                yield return new PointRecord(x, y, z, intensity, System.Windows.Media.Color.FromRgb(r, g, b));
            }
        }

        /// <summary>标记为已上传，释放原始数据</summary>
        public void MarkAsUploaded()
        {
            IsUploaded = true;
            // 释放原始点数据，只保留量化数据
            _originalPoints = null;
        }

        /// <summary>获取量化数据（用于GPU上传）</summary>
        public byte[]? GetQuantizedData() => _quantizedPoints;
    }
}

